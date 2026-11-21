using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Storylets;
using Web.Forms;

namespace Web.Tests;

// A test ship founded as a legacy in memory (1f part one, Task 5): the crew and traits modules' own views on it read the ship as
// TestShipSource reads it, so the launch's team building sees the ship the editor's previews see.
public sealed class TestShipLegacyTests
{
    public static TheoryData<string> Packs => new() { "content", "tests/fixtures/content" };

    private static (LivePack Live, IReadOnlyList<TestShipDef> Ships) Load(string folder)
    {
        string root = Path.Combine(TestApp.RepositoryRoot(), folder);
        ContentFileSet files = ContentFileSet.ReadDirectory(root);
        ContentLoadResult result = ContentLoader.Load(files.Files);
        ContentPack pack = result.Pack ?? throw new InvalidOperationException($"{folder} did not load: " + string.Join(" | ", result.Errors.Select(e => e.Message)));
        TestShipsLoadResult ships = ContentLoader.LoadTestShips(files.Files, pack);
        Assert.Empty(ships.Errors);
        Assert.NotEmpty(ships.Ships);
        return (new ContentHost(pack).Current!, ships.Ships);
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void The_founded_legacys_roster_and_trait_views_read_every_ship_as_TestShipSource_does(string folder)
    {
        (LivePack live, IReadOnlyList<TestShipDef> ships) = Load(folder);
        ContentPack pack = live.Pack;
        foreach (TestShipDef ship in ships)
        {
            LegacyState state = TestShipLegacy.Found(live, ship);
            var source = new TestShipSource(pack, ship);
            IRosterView roster = live.Game.Bus.View<IRosterView>(state);
            ITraitView traits = live.Game.Bus.View<ITraitView>(state);

            Assert.Equal(ship.Date, state.Day);
            Assert.Equal(ship.Cycle, state.Cycle);
            Assert.Equal(source.Roster, roster.Roster);
            foreach (CrewDef crew in pack.Crew)
            {
                string at = $"{ship.Id}, {crew.Id}";
                // The launch reads fitness only: someone not aboard can no more be sent than someone unfit.
                Assert.True((source.IsFit(crew.Id) && source.IsAboard(crew.Id)) == roster.IsFit(crew.Id), $"{at}: fit and aboard");
                if (!source.Roster.Contains(crew.Id))
                {
                    Assert.Null(roster.Member(crew.Id));
                    continue;
                }

                Assert.True(source.Morale(crew.Id) == roster.Morale(crew.Id), $"{at}: morale");
                Assert.True(source.Loyalty(crew.Id) == roster.Loyalty(crew.Id), $"{at}: loyalty");
                TeamMember member = roster.Member(crew.Id)!;
                Assert.Equal(pack.CrewTuning.MoraleBonus(source.Morale(crew.Id)), member.MoraleBonus);
                Assert.All(pack.Skills, s => Assert.True(source.Skill(crew.Id, s.Id) == member.Skills.GetValueOrDefault(s.Id), $"{at}: {s.Id}"));
            }

            // Every trait on the ship, every crew member, and every pair, held or not.
            string?[] holders = new string?[] { null }.Concat(pack.Crew.Select(c => c.Id)).ToArray();
            foreach (TraitDef def in pack.Traits)
            {
                foreach (string? on in holders)
                {
                    foreach (string? toward in holders)
                    {
                        Assert.True(source.Trait(def.Id, on, toward) == traits.Value(def.Id, on, toward), $"{ship.Id}: {def.Id} on {on ?? "ship"} toward {toward ?? "nobody"}");
                    }
                }
            }

            // Each held trait's when, counted back from the ship's today as the ship writes it.
            foreach (TestShipTrait trait in ship.Traits)
            {
                TraitHeld held = Assert.Single(traits.Held(trait.On), h => h.Trait == trait.Trait && h.TargetId == trait.Toward);
                Assert.Equal((trait.Value, ship.Date - trait.DaysAgo, ship.Cycle - trait.CyclesAgo), (held.Value, held.ChangedAtDay, held.ChangedAtCycle));
            }

            Assert.Equal(ship.Traits.Count, holders.Sum(h => traits.Held(h).Count));
        }
    }

    [Fact]
    public void The_fixtures_ship_founds_its_unfit_its_away_and_its_dead_as_the_launch_must_read_them()
    {
        (LivePack live, IReadOnlyList<TestShipDef> ships) = Load("tests/fixtures/content");
        LegacyState state = TestShipLegacy.Found(live, ships.Single(s => s.Id == "test_ship_a"));
        IRosterView roster = live.Game.Bus.View<IRosterView>(state);

        Assert.False(roster.IsFit("CREW_03"));
        Assert.False(roster.IsFit("CREW_04"));
        Assert.True(roster.IsFit("CREW_01"));
        Assert.DoesNotContain("CREW_07", roster.Roster);
        Assert.Contains("CREW_04", roster.Roster);
    }

    [Fact]
    public async Task Founding_writes_nothing_to_the_store()
    {
        using var app = new TestApp();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);
        int events = app.Game.Store.ReadEvents(id).Count;
        TestShipsLoadResult ships = ContentLoader.LoadTestShips(app.Editor.Read().Files, app.Live.Pack);

        foreach (TestShipDef ship in ships.Ships)
        {
            TestShipLegacy.Found(app.Live, ship);
        }

        Assert.NotEmpty(ships.Ships);
        Assert.Single(app.Game.Store.ListLegacies());
        Assert.Equal(events, app.Game.Store.ReadEvents(id).Count);
    }
}

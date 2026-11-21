using System.Text.Json;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;
using Sim.Storylets;

namespace Sim.Tests;

// Test ships (1e part six, design 4.6): a ship described in content, loaded apart from the pack, read through IStateSource, and
// written so that every storylet and vignette in the game's pack can be cast on one.
public class TestShipTests : IClassFixture<RepoContentGuard>
{
    private const string ShipsFile = "testships/base.yaml";

    private static Dictionary<string, string> GameFiles()
    {
        string content = Path.Combine(TestContent.Root, "content");
        return Directory.EnumerateFiles(content, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(content, f).Replace('\\', '/'), File.ReadAllText);
    }

    private static ContentPack PackOf(Dictionary<string, string> files)
    {
        ContentLoadResult result = ContentLoader.Load(files);
        return result.Pack ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
    }

    private static TestShipsLoadResult Ships(string text)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files[ShipsFile] = text;
        return ContentLoader.LoadTestShips(files, TestContent.Pack);
    }

    private static string Refused(string text)
    {
        TestShipsLoadResult result = Ships(text);
        Assert.Empty(result.Ships);
        ContentError error = Assert.Single(result.Errors);
        Assert.Equal(ShipsFile, error.File);
        return error.Message;
    }

    // Review focus 4: a test ship never reaches the pack, so a broken one leaves the game's pack loading as it did.
    [Fact]
    public void The_pack_loader_skips_test_ships_however_broken()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        string packBefore = JsonSerializer.Serialize(ContentLoader.Load(files).Pack, SimJson.Options);
        files[ShipsFile] = "- { id: Not A Ship, crew: 12 }\n: not yaml [";

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Empty(result.Errors);
        Assert.Equal(packBefore, JsonSerializer.Serialize(result.Pack, SimJson.Options));
    }

    [Fact]
    public void Both_packs_test_ships_load()
    {
        Dictionary<string, string> game = GameFiles();
        TestShipsLoadResult ours = ContentLoader.LoadTestShips(game, PackOf(game));
        TestShipsLoadResult fixture = ContentLoader.LoadTestShips(TestContent.RepoFiles(), TestContent.Pack);

        Assert.Empty(ours.Errors);
        Assert.Equal(["uss_quality_assurance", "uss_quality_control"], ours.Ships.Select(s => s.Id));
        Assert.Equal("USS Quality Assurance", ours.Ships[0].Name);
        Assert.Empty(fixture.Errors);
        Assert.Equal("test_ship_a", Assert.Single(fixture.Ships).Id);
    }

    // The editor reads the test ships apart, against the pack Validate built: a broken one leaves the pack valid, naming the ship
    // and the field.
    [Fact]
    public void The_editor_reads_test_ships_apart_from_the_pack()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files[ShipsFile] = "- { id: test_ship_a, name: A, date: 10, cycle: 2, crew: [{ id: CREW_99 }] }\n";
        var set = new ContentFileSet(files);

        Assert.NotNull(set.Validate().Pack);
        Assert.Empty(set.Validate().Errors);
        Assert.Equal(new ContentError(ShipsFile, "test_ship_a: crew[0].id: no crew member CREW_99 in the starting crew."), Assert.Single(set.TestShips().Errors));
        Assert.Single(new ContentFileSet(TestContent.RepoFiles()).TestShips().Ships);
    }

    // Step 5: every storylet and vignette in the game's pack casts, with a fixed stream, on at least one of the game's test ships.
    [Fact]
    public void Every_storylet_and_vignette_in_the_game_casts_on_a_test_ship()
    {
        Dictionary<string, string> files = GameFiles();
        ContentPack pack = PackOf(files);
        TestShipsLoadResult ships = ContentLoader.LoadTestShips(files, pack);
        Assert.Empty(ships.Errors);
        var casting = new Casting(pack);
        var failures = new List<string>();
        foreach (string id in pack.Storylets.Select(s => s.Id).Concat(pack.Vignettes.Select(v => v.Id)))
        {
            var why = new List<string>();
            bool cast = false;
            foreach (TestShipDef ship in ships.Ships)
            {
                var source = new TestShipSource(pack, ship);
                CastResult result = casting.Cast(id, source, source.Roster, () => RngStream.For(1, ship.Cycle, $"cast:{id}:{ship.Cycle}"), pack.CastTries, _ => false);
                if (result.Outcome == CastOutcome.Cast)
                {
                    cast = true;
                    break;
                }

                FailedClause? failed = casting.Failed(id, source, source.Roster);
                why.Add($"{ship.Id}: {result.Outcome}, {failed?.Slot} {failed?.Clause}");
            }

            if (!cast)
            {
                failures.Add($"{id} casts on no test ship ({string.Join("; ", why)})");
            }
        }

        Assert.Empty(failures);
    }

    // The fixture's ship is for its own items: all but STORYLET_05, which never casts, and VIGNETTE_03, which needs trait_a at 2.
    [Fact]
    public void The_fixtures_ship_casts_what_its_notes_say()
    {
        ContentPack pack = TestContent.Pack;
        TestShipDef ship = Assert.Single(ContentLoader.LoadTestShips(TestContent.RepoFiles(), pack).Ships);
        var source = new TestShipSource(pack, ship);
        var casting = new Casting(pack);

        List<string> cast = pack.Storylets.Select(s => s.Id).Concat(pack.Vignettes.Select(v => v.Id))
            .Where(id => casting.Cast(id, source, source.Roster, () => RngStream.For(1, ship.Cycle, $"cast:{id}:{ship.Cycle}"), pack.CastTries, _ => false).Outcome == CastOutcome.Cast)
            .ToList();

        Assert.Equal(["STORYLET_01", "STORYLET_02", "STORYLET_03", "STORYLET_04", "STORYLET_06", "VIGNETTE_01", "VIGNETTE_02", "VIGNETTE_04"], cast);
    }

    // A ship written by hand against the fixture pack, so each reading can be checked on its own.
    private static readonly TestShipDef Hand = new(
        "hand",
        "Hand",
        500,
        10,
        [
            new TestShipCrew("CREW_01", 30, 70, new Dictionary<string, int> { ["SKILL_A"] = 9 }, false, null, null),
            new TestShipCrew("CREW_02", null, null, null, null, false, null),
            new TestShipCrew("CREW_03", null, null, null, null, null, false),
        ],
        [
            new TestShipTrait("trait_a", null, null, 2, 15, 3),
            new TestShipTrait("trait_b", "CREW_01", null, 4, 6, 1),
            new TestShipTrait("trait_g", "CREW_01", "CREW_02", 3, 20, 4),
        ],
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>
        {
            ["CREW_01"] = new Dictionary<string, IReadOnlyList<int>> { ["sent"] = [1, 3, 8], ["hurt"] = [2] },
        },
        [
            new TestShipEntry("death", 0, ["CREW_03"]),
            new TestShipEntry("death", 30, ["CREW_08"]),
            new TestShipEntry("call", 5, ["CREW_01", "CREW_02"]),
        ],
        [new TestShipArrival("STORYLET_01", 40, 7)]);

    [Fact]
    public void The_source_reads_crew_as_written_and_everyone_else_as_they_started()
    {
        ContentPack pack = TestContent.Pack;
        var source = new TestShipSource(pack, Hand);
        CrewDef starting = pack.Crew.Single(c => c.Id == "CREW_02");

        Assert.Equal(10, source.Cycle);
        Assert.Equal(30, source.Morale("CREW_01"));
        Assert.Equal(70, source.Loyalty("CREW_01"));
        Assert.Equal(pack.CrewTuning.MoraleResting, source.Morale("CREW_02"));
        Assert.Equal(pack.CrewTuning.StartingLoyalty, source.Loyalty("CREW_02"));
        Assert.Equal(9, source.Skill("CREW_01", "SKILL_A"));
        Assert.Equal(pack.Crew.Single(c => c.Id == "CREW_01").Skills["SKILL_B"], source.Skill("CREW_01", "SKILL_B"));
        Assert.Equal(starting.Skills["SKILL_A"], source.Skill("CREW_02", "SKILL_A"));
        Assert.False(source.IsFit("CREW_01"));
        Assert.True(source.IsFit("CREW_02"));
        Assert.True(source.IsAboard("CREW_01"));
        Assert.False(source.IsAboard("CREW_02"));
    }

    [Fact]
    public void The_dead_are_neither_aboard_nor_on_the_roster()
    {
        var source = new TestShipSource(TestContent.Pack, Hand);

        Assert.False(source.IsAboard("CREW_03"));
        Assert.DoesNotContain("CREW_03", source.Roster);
        Assert.Equal(TestContent.Pack.Crew.Select(c => c.Id).Where(id => id != "CREW_03"), source.Roster);
        // Away is on the roster, as the module's living crew are, but not aboard.
        Assert.Contains("CREW_02", source.Roster);
    }

    [Fact]
    public void The_source_reads_traits_on_the_ship_a_crew_member_and_a_pair()
    {
        var source = new TestShipSource(TestContent.Pack, Hand);

        Assert.Equal(2, source.Trait("trait_a", null));
        Assert.Equal(0, source.Trait("trait_a", "CREW_01"));
        Assert.Equal(4, source.Trait("trait_b", "CREW_01"));
        Assert.Equal(0, source.Trait("trait_b", "CREW_02"));
        Assert.Equal(3, source.Trait("trait_g", "CREW_01", "CREW_02"));
        Assert.Equal(0, source.Trait("trait_g", "CREW_02", "CREW_01"));
        Assert.Equal(0, source.Trait("trait_g", "CREW_01"));
        Assert.Equal(new Since(3, 15), source.TraitSince("trait_a", null));
        Assert.Equal(new Since(1, 6), source.TraitSince("trait_b", "CREW_01"));
        Assert.Null(source.TraitSince("trait_b", "CREW_02"));
        // A pair's trait has no clock of its own to read, as StateSource reads none.
        Assert.Null(source.TraitSince("trait_g", "CREW_01"));
    }

    [Fact]
    public void The_source_counts_history_within_the_window()
    {
        var source = new TestShipSource(TestContent.Pack, Hand);

        Assert.Equal(1, source.History("CREW_01", "sent", 1));
        Assert.Equal(1, source.History("CREW_01", "sent", 2));
        Assert.Equal(2, source.History("CREW_01", "sent", 3));
        Assert.Equal(3, source.History("CREW_01", "sent", 20));
        Assert.Equal(0, source.History("CREW_01", "hurt", 1));
        Assert.Equal(1, source.History("CREW_01", "hurt", 2));
        Assert.Equal(0, source.History("CREW_01", "rested", 20));
        Assert.Equal(0, source.History("CREW_02", "sent", 20));
    }

    [Fact]
    public void The_source_reads_arrivals_and_the_chronicle_with_today_included()
    {
        var source = new TestShipSource(TestContent.Pack, Hand);

        Assert.Equal(new Since(7, 40), source.StoryletSince("STORYLET_01"));
        Assert.Null(source.StoryletSince("STORYLET_02"));
        Assert.Equal(1, source.ChronicleCount("death", 1));
        Assert.Equal(1, source.ChronicleCount("death", 30));
        Assert.Equal(2, source.ChronicleCount("death", 31));
        Assert.Equal(1, source.ChronicleCount("call", 6));
        Assert.Equal(0, source.ChronicleCount("call", 5));
        Assert.Equal(1, source.ChronicleCount("CREW_02", "call", 30));
        Assert.Equal(0, source.ChronicleCount("CREW_02", "death", 30));
        Assert.Equal(1, source.ChronicleCount("CREW_08", "death", 31));
    }

    // The expressions read a test ship through the same views the module's state goes through.
    [Fact]
    public void An_expression_reads_a_test_ship_as_it_reads_the_game()
    {
        ContentPack pack = TestContent.Pack;
        var book = new ExpressionBook(pack.Traits, pack.Skills.Select(s => s.Id), HistoryKinds.All, pack.HistoryCycles, pack.Storylets.Select(s => s.Id), pack.Vignettes.Select(v => v.Id));
        var source = new TestShipSource(pack, Hand);
        var cast = new Dictionary<string, string> { ["subject"] = "CREW_01", ["target"] = "CREW_02" };
        string[] holds =
        [
            "ship.trait.trait_a == 2 && subject.trait.trait_b >= 4 && subject.trait.trait_g.toward(target) == 3",
            "subject.history.count('sent', 3) == 2 && !subject.history.last('hurt') && subject.skills.SKILL_A == 9",
            "chronicle.count('death', 31) == 2 && storylet.STORYLET_01.daysSince >= 40 && subject.trait.trait_b.cyclesSince == 1",
            "subject.aboard && !target.aboard && !subject.fit && cycle.number == 10",
        ];

        foreach (string text in holds)
        {
            CheckedExpression expression = book.Check(text, ["subject", "target"], out ExpressionError? error) ?? throw new InvalidOperationException(error!.Message);
            Assert.True(book.Evaluate(expression, source, cast), text);
        }
    }

    public static TheoryData<string, string> RefusedShips => new()
    {
        { "crew: [{ id: CREW_99 }]", "crew[0].id: no crew member CREW_99 in the starting crew." },
        { "traits: [{ trait: trait_zz, value: 1, daysAgo: 0, cyclesAgo: 0 }]", "traits[0].trait: no trait trait_zz in traits/." },
        { "traits: [{ trait: trait_e, on: CREW_01, value: level_z, daysAgo: 0, cyclesAgo: 0 }]", "traits[0].value: trait_e has no level named 'level_z'; its levels are level_a and level_b." },
        { "history: { CREW_01: { napped: [1] } }", "history.CREW_01.napped: no history kind named napped; the kinds are sent, hurt, rested, lost_crewmate, lost_teammate, lost_mission." },
        { "chronicle: [{ kind: wedding, daysAgo: 1, subjects: [] }]", "chronicle[0].kind: no chronicle kind named wedding; the kinds are founding, death, mission, call, storylet." },
        { "storylets: [{ id: STORYLET_99, daysAgo: 1, cyclesAgo: 1 }]", "storylets[0].id: no storylet or vignette STORYLET_99." },
        { "traits: [{ trait: trait_e, on: CREW_01, toward: CREW_02, value: 1, daysAgo: 0, cyclesAgo: 0 }]", "traits[0].toward: A trait on a pair needs a definition with on: pair." },
        { "traits: [{ trait: trait_e, value: 1, daysAgo: 0, cyclesAgo: 0 }]", "traits[0].on: trait_e sits on a crew member, not on the ship. Say who holds it with on." },
        { "traits: [{ trait: trait_g, on: CREW_01, value: 1, daysAgo: 0, cyclesAgo: 0 }]", "traits[0].on: trait_g sits on a pair, so it is used with toward." },
        { "traits: [{ trait: trait_a, value: 0, daysAgo: 0, cyclesAgo: 0 }]", "traits[0].value: a held trait's value is 1 or more, or a level's name; a trait at 0 is not held, so leave it out." },
        { "traits: [{ trait: trait_a, value: 1, daysAgo: -1, cyclesAgo: 0 }]", "traits[0].daysAgo: a whole number of days ago, 0 or more." },
        { "storylets: [{ id: STORYLET_01, daysAgo: 40, cyclesAgo: 3 }]", "storylets[0].cyclesAgo: a whole number of cycles ago, 0 to the ship's cycle (2)." },
        { "history: { CREW_01: { sent: [0] } }", "history.CREW_01.sent: each is how many cycles ago it happened: 1 (last cycle) to 2, the most the history keeps here." },
    };

    [Theory]
    [MemberData(nameof(RefusedShips))]
    public void The_loader_refuses_what_the_pack_does_not_have(string change, string message)
    {
        Assert.Equal($"test_ship_a: {message}", Refused($"- {{ id: test_ship_a, name: A, date: 10, cycle: 2, {change} }}\n"));
    }

    // The fixture has no trait that sits on the ship alone; the game's sensor quirk does.
    [Fact]
    public void The_loader_refuses_a_ship_trait_on_a_crew_member()
    {
        Dictionary<string, string> files = GameFiles();
        files[ShipsFile] = "- { id: ship, name: A, date: 10, cycle: 2, traits: [{ trait: sensors_quirk, on: CREW_01, value: noticed, daysAgo: 0, cyclesAgo: 0 }] }\n";

        TestShipsLoadResult result = ContentLoader.LoadTestShips(files, PackOf(files));

        Assert.Equal(new ContentError(ShipsFile, "ship: traits[0].on: sensors_quirk sits on the ship, not on a crew member."), Assert.Single(result.Errors));
    }

    [Fact]
    public void A_ship_needs_a_name_a_date_and_a_cycle()
    {
        TestShipsLoadResult result = Ships("- { id: test_ship_a }\n");

        Assert.Empty(result.Ships);
        Assert.Equal(
            ["test_ship_a: name: a test ship needs a name.", "test_ship_a: date: the ship's today, as a day number: a whole number of 0 or more.", "test_ship_a: cycle: the cycle now starting: a whole number of 1 or more."],
            result.Errors.Select(e => e.Message));
    }

    // Edited as text, but "Save as test ship" (Task 14) writes the file, so it must come back through the writer unchanged.
    [Theory]
    [InlineData("content")]
    [InlineData("tests/fixtures/content")]
    public void A_test_ship_file_round_trips_through_the_writer(string root)
    {
        string content = Path.Combine(TestContent.Root, root);
        Dictionary<string, string> files = Directory.EnumerateFiles(content, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(content, f).Replace('\\', '/'), File.ReadAllText);
        var set = new ContentFileSet(files);
        ContentPack pack = PackOf(files);

        Assert.Empty(ContentEdits.Losses(set, ShipsFile));
        Assert.Contains(set.Index(), i => i.Kind == ContentKind.TestShip && !i.Lossy);
        ContentFileSet written = ContentEdits.Canonicalize(set);

        Assert.Equal(files[ShipsFile].ReplaceLineEndings("\n"), written.Files[ShipsFile]);
        Assert.Equal(
            JsonSerializer.Serialize(ContentLoader.LoadTestShips(files, pack).Ships, SimJson.Options),
            JsonSerializer.Serialize(ContentLoader.LoadTestShips(written.Files.ToDictionary(), pack).Ships, SimJson.Options));
    }

    [Fact]
    public void A_ship_model_written_reads_back_equal()
    {
        var model = new TestShipModel(
            "test_ship_b",
            "TEST_SHIP_B",
            37500,
            6,
            [new TestShipCrewModel("CREW_01", 40, 55, new Dictionary<string, int> { ["SKILL_A"] = 7 }, false, true, null), new TestShipCrewModel("CREW_02", Alive: false)],
            [new TestShipTraitModel("trait_e", "CREW_01", null, "level_b", 3, 1), new TestShipTraitModel("trait_g", "CREW_01", "CREW_03", "2", 9, 2)],
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> { ["CREW_01"] = new Dictionary<string, IReadOnlyList<int>> { ["sent"] = [1, 2], ["hurt"] = [2] } },
            [new TestShipEntryModel("death", 4, ["CREW_02"])],
            [new TestShipArrivalModel("STORYLET_02", 12, 2)],
            "Two lines\nof notes.");
        string text = ContentWriter.Render(null, [model]);
        Dictionary<string, string> files = TestContent.RepoFiles();
        files[ShipsFile] = text;

        TestShipModel back = ContentEdits.Get<TestShipModel>(new ContentFileSet(files), ContentKind.TestShip, "test_ship_b")!;

        Assert.Equal(JsonSerializer.Serialize(model), JsonSerializer.Serialize(back));
        Assert.Empty(ContentEdits.Losses(new ContentFileSet(files), ShipsFile));
        Assert.Empty(ContentLoader.LoadTestShips(files, TestContent.Pack).Errors);
    }
}

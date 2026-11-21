using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Persistence;
using Sim.Content;
using Sim.Core;
using Sim.Crew;
using Sim.Missions;
using Web.Tuning;

namespace Web.Tests;

// The bench's real teams (1f part one, Task 6; design 4.5): a team from a legacy or a test ship is the team the launch would send for
// the same picks, a member the launch would refuse is listed so and cannot be built, and benching reads a legacy without writing it.
public sealed class BenchTeamsTests : IDisposable
{
    private static readonly string[] Pair = ["CREW_01", "CREW_02"];

    private readonly TestApp app = new();

    private BenchTeams Teams => app.Services.GetRequiredService<BenchTeams>();

    public void Dispose()
    {
        app.Dispose();
    }

    private static string Json(object value)
    {
        return JsonSerializer.Serialize(value, SimJson.Options);
    }

    private static CrewMember Crew(LegacyState state, string id)
    {
        return state.Module<CrewState>(CrewModule.Name).Members.Single(m => m.Id == id);
    }

    // A legacy at the briefing on the fixture: CREW_01 injured and holding trait_b, CREW_02 at low morale and holding trait_g toward
    // CREW_01 (a pair with a check bonus when CREW_01 is on the team), CREW_03 recovering, and the ship holding trait_a.
    private async Task<Guid> Conditioned()
    {
        Guid id = await app.CreateAtOrders("LEGACY_BENCH", 1);
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != CyclePhase.Briefing)
            {
                legacy.Send(new AdvancePhase());
            }

            Crew(legacy.State, "CREW_01").LightInjuriesHealAtDay.Add(legacy.State.Day + 1000);
            Crew(legacy.State, "CREW_02").Morale = 0;
            Crew(legacy.State, "CREW_03").Status = CrewStatus.Recovering;
            legacy.Send(new SetTrait("trait_b", "CREW_01", null, 2));
            legacy.Send(new SetTrait("trait_g", "CREW_02", "CREW_01", 3));
            legacy.Send(new SetTrait("trait_a", null, null, 1));
            return legacy;
        });
        return id;
    }

    [Fact]
    public async Task A_team_from_a_legacy_is_the_team_the_launch_sends_for_the_same_picks()
    {
        Guid id = await Conditioned();
        (Team? built, string? refused) = await Teams.Build(BenchTeams.LegacyKind, id.ToString(), Pair, "CREW_01", CancellationToken.None);

        // The conditions reach the team.
        Assert.Null(refused);
        Assert.NotNull(built);
        TeamMember injured = built.Members[0];
        TeamMember low = built.Members[1];
        LoadedLegacy before = (await app.Sessions.Read(app.Live, id))!;
        Assert.All(Crew(before.State, "CREW_01").Skills, s => Assert.Equal(Math.Max(0, s.Value.Level - 1), injured.Skills[s.Key]));
        Assert.Equal(new Dictionary<string, int> { ["trait_b"] = 2 }, injured.Traits!);
        Assert.Equal(app.Live.Pack.CrewTuning.MoraleBonus(0), low.MoraleBonus);
        Assert.True(low.MoraleBonus < 0);
        Assert.Equal([new TraitBonus("trait_g", -1)], low.TraitBonuses!);
        Assert.Equal(new Dictionary<string, int> { ["trait_a"] = 1 }, built.ShipTraits!);

        // And it is the team the resolver starts with, once the same picks are launched.
        MissionState mission = (await app.Sessions.Run(app.Live, id, legacy =>
        {
            string objective = legacy.State.Module<MissionsState>(MissionsModule.Name).Site!.Objectives[0].Id;
            legacy.Send(new AssignTeam(objective, Pair, "CREW_01", [], new Parameters()));
            while (legacy.State.Phase != CyclePhase.Resolution)
            {
                legacy.Send(new AdvancePhase());
            }

            return legacy.State.Module<MissionsState>(MissionsModule.Name).Site!.Mission!.State;
        }))!;
        Assert.Equal(Json(built.Members), Json(mission.Team.Select(m => m.Member).ToList()));
        Assert.Equal("CREW_01", mission.LeadId);
        Assert.Equal(Json(built.ShipTraits!), Json(mission.ShipTraits!));
    }

    [Fact]
    public async Task A_member_the_launch_would_refuse_is_listed_with_the_refusal_and_cannot_be_built()
    {
        Guid id = await Conditioned();
        BenchSource source = (await Teams.Legacy(id, CancellationToken.None))!;
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;

        Assert.Equal((BenchTeams.LegacyKind, id.ToString(), "LEGACY_BENCH"), (source.Kind, source.Id, source.Name));
        Assert.Equal(app.Live.Game.Bus.View<IRosterView>(legacy.State).Roster, source.Candidates.Select(c => c.Id));
        Assert.Equal(app.Live.Names.Crew("CREW_01"), source.Candidates.Single(c => c.Id == "CREW_01").Name);
        Assert.Equal("Crew member 'CREW_03' is not fit.", source.Candidates.Single(c => c.Id == "CREW_03").Refused);
        Assert.All(source.Candidates.Where(c => c.Id != "CREW_03"), c => Assert.Null(c.Refused));

        // The launch's checks, in its words; and nobody, or no such source, is no team.
        Assert.Equal((null, "Crew member 'CREW_03' is not fit."), await Build(id, ["CREW_01", "CREW_03"], "CREW_01"));
        Assert.Equal((null, "Crew member 'CREW_01' is listed twice."), await Build(id, ["CREW_01", "CREW_01"], "CREW_01"));
        Assert.Equal((null, "Crew member 'NOBODY' is not on the roster."), await Build(id, ["CREW_01", "NOBODY"], "CREW_01"));
        Assert.Equal((null, "The lead 'CREW_04' is not on the team."), await Build(id, Pair, "CREW_04"));
        Assert.Equal((null, "Nobody is picked."), await Build(id, [], "CREW_01"));
        Guid unknown = Guid.NewGuid();
        Assert.Equal((null, $"No legacy '{unknown}'."), await Build(unknown, Pair, "CREW_01"));
        Assert.Null(await Teams.Legacy(unknown, CancellationToken.None));
    }

    // On the game's pack, whose test ship has a grieving member; the content is only read.
    [Fact]
    public async Task A_team_from_a_test_ships_grieving_member_carries_the_trait()
    {
        string root = Path.Combine(TestApp.RepositoryRoot(), "content");
        var host = new ContentHost(ContentLoader.LoadDirectory(root).Pack!);
        var teams = new BenchTeams(app.Sessions, host, new ContentEditor(host, new ContentFolder(root)));
        BenchSource ship = teams.TestShip("uss_quality_assurance")!;
        (Team? team, string? refused) = await teams.Build(BenchTeams.TestShipKind, "uss_quality_assurance", ["CREW_04", "CREW_06"], "CREW_04", CancellationToken.None);

        Assert.Equal((BenchTeams.TestShipKind, "USS Quality Assurance"), (ship.Kind, ship.Name));
        // The dead are off the roster.
        Assert.DoesNotContain(ship.Candidates, c => c.Id is "CREW_07" or "CREW_08");
        Assert.All(ship.Candidates, c => Assert.Null(c.Refused));
        Assert.Null(refused);
        Assert.NotNull(team);
        TeamMember grieving = team.Members[0];
        Assert.Equal(1, grieving.Traits!["grieving"]);
        Assert.Equal([new TraitBonus("avoids", -1)], grieving.TraitBonuses!);
        Assert.Equal(new Dictionary<string, int> { ["sensors_quirk"] = 2 }, team.ShipTraits!);
        Assert.Null(teams.TestShip("no_such_ship"));
        Assert.Equal((null, "No test ship 'no_such_ship'."), await teams.Build(BenchTeams.TestShipKind, "no_such_ship", ["CREW_04"], "CREW_04", CancellationToken.None));
    }

    // The fixture's ship lists CREW_03 as not fit and CREW_04 as away: the launch would refuse both alike.
    [Fact]
    public async Task A_test_ships_unfit_and_away_members_are_listed_with_the_refusal_and_cannot_be_built()
    {
        BenchSource ship = Teams.TestShip("test_ship_a")!;

        Assert.Equal("Crew member 'CREW_03' is not fit.", ship.Candidates.Single(c => c.Id == "CREW_03").Refused);
        Assert.Equal("Crew member 'CREW_04' is not fit.", ship.Candidates.Single(c => c.Id == "CREW_04").Refused);
        Assert.Null(ship.Candidates.Single(c => c.Id == "CREW_01").Refused);
        Assert.Equal((null, "Crew member 'CREW_04' is not fit."), await Teams.Build(BenchTeams.TestShipKind, "test_ship_a", ["CREW_01", "CREW_04"], "CREW_01", CancellationToken.None));
    }

    [Fact]
    public async Task Benching_a_legacy_leaves_its_record_and_event_log_byte_for_byte_the_same_and_its_lock_free()
    {
        Guid id = await Conditioned();
        string before = LegacyExport.Export(app.Game.Store, id, app.Live.Game.ModuleTypes);
        int locks = app.Sessions.LockCount;

        Assert.NotNull(await Teams.Legacy(id, CancellationToken.None));
        Assert.NotNull((await Teams.Build(BenchTeams.LegacyKind, id.ToString(), Pair, "CREW_01", CancellationToken.None)).Team);
        Assert.Null(await Teams.Legacy(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(before, LegacyExport.Export(app.Game.Store, id, app.Live.Game.ModuleTypes));
        // No lock is kept for the unknown legacy, and the legacy's own is not held: a run takes it at once.
        Assert.Equal(locks, app.Sessions.LockCount);
        using var soon = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.NotNull(await app.Sessions.Run(app.Live, id, legacy => legacy, soon.Token));
    }

    private async Task<(Team? Team, string? Refused)> Build(Guid id, IReadOnlyList<string> crewIds, string leadId)
    {
        return await Teams.Build(BenchTeams.LegacyKind, id.ToString(), crewIds, leadId, CancellationToken.None);
    }
}

using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;
using Sim.Missions;
using Sim.Traits;

namespace Sim.Tests;

// Slice 1f, Task 4: the launch's team building, lifted out of MissionsModule so the bench can call it.
public class LaunchTeamTests
{
    private static readonly string[] Pair = ["CREW_01", "CREW_02"];

    private static readonly ContentPack Pack = TestContent.Pack with
    {
        Traits =
        [
            TraitDef.Plain("steady") with { On = TraitPlaces.Crew, Mission = new TraitMission(2, false) },
            TraitDef.Plain("covers") with { On = TraitPlaces.Pair, Mission = new TraitMission(3, true) },
            TraitDef.Plain("hull_scar") with { On = TraitPlaces.Ship },
        ],
    };

    private static (EventBus Bus, LegacyState State) AtBriefing()
    {
        var bus = new EventBus();
        CycleModule.Register(bus, Pack);
        CrewModule.Register(bus, Pack);
        MissionsModule.Register(bus, Pack);
        TraitsModule.Register(bus, Pack);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };
        bus.Send(state, new FoundLegacy());
        while (state.Phase != CyclePhase.Briefing)
        {
            bus.Send(state, new AdvancePhase());
        }

        return (bus, state);
    }

    private static CrewMember Crew(LegacyState state, string id)
    {
        return state.Module<CrewState>(CrewModule.Name).Members.Single(m => m.Id == id);
    }

    private static string Json(object value)
    {
        return JsonSerializer.Serialize(value, SimJson.Options);
    }

    // CREW_01 is injured and holds a crew trait with a bonus; CREW_02 is at low morale and covers CREW_01 (a pair with a bonus when
    // CREW_01 is on the team); the ship holds a trait.
    private static (EventBus Bus, LegacyState State) Conditioned()
    {
        (EventBus bus, LegacyState state) = AtBriefing();
        Crew(state, "CREW_01").LightInjuriesHealAtDay.Add(state.Day + 1000);
        Crew(state, "CREW_02").Morale = 0;
        bus.Send(state, new SetTrait("steady", "CREW_01", null, 1));
        bus.Send(state, new SetTrait("covers", "CREW_02", "CREW_01", 1));
        bus.Send(state, new SetTrait("hull_scar", null, null, 2));
        return (bus, state);
    }

    [Fact]
    public void Build_gives_the_team_the_launch_sends()
    {
        (EventBus bus, LegacyState state) = Conditioned();
        Team built = LaunchTeam.Build(bus.View<IRosterView>(state), bus.View<ITraitView>(state), Pair, "CREW_01");

        // The conditions reach the team.
        TeamMember injured = built.Members[0];
        TeamMember low = built.Members[1];
        Assert.All(Crew(state, "CREW_01").Skills, s => Assert.Equal(Math.Max(0, s.Value.Level - 1), injured.Skills[s.Key]));
        Assert.Equal(new Dictionary<string, int> { ["steady"] = 1 }, injured.Traits!);
        Assert.Equal([new TraitBonus("steady", 2)], injured.TraitBonuses!);
        Assert.Equal(Pack.CrewTuning.MoraleBonus(0), low.MoraleBonus);
        Assert.True(low.MoraleBonus < 0);
        Assert.Null(low.Traits);
        Assert.Equal([new TraitBonus("covers", 3)], low.TraitBonuses!);
        Assert.Equal(new Dictionary<string, int> { ["hull_scar"] = 2 }, built.ShipTraits!);

        // And it is the team the resolver started with.
        AssignTeam assign = new(state.Module<MissionsState>(MissionsModule.Name).Site!.Objectives[0].Id, Pair, "CREW_01", [], new Parameters());
        bus.Send(state, assign);
        while (state.Phase != CyclePhase.Resolution)
        {
            bus.Send(state, new AdvancePhase());
        }

        MissionState mission = state.Module<MissionsState>(MissionsModule.Name).Site!.Mission!.State;
        Assert.Equal(Json(built.Members), Json(mission.Team.Select(m => m.Member).ToList()));
        Assert.Equal("CREW_01", mission.LeadId);
        Assert.Equal(Json(built.ShipTraits!), Json(mission.ShipTraits!));
    }

    [Fact]
    public void Refusal_names_an_unknown_and_an_unfit_member_and_passes_a_fit_one()
    {
        (EventBus bus, LegacyState state) = AtBriefing();
        Crew(state, "CREW_02").Status = CrewStatus.Recovering;
        IRosterView roster = bus.View<IRosterView>(state);

        Assert.Equal("Crew member 'NOBODY' is not on the roster.", LaunchTeam.Refusal(roster, "NOBODY"));
        Assert.Equal("Crew member 'CREW_02' is not fit.", LaunchTeam.Refusal(roster, "CREW_02"));
        Assert.Null(LaunchTeam.Refusal(roster, "CREW_01"));
    }

    [Fact]
    public void SizeRefusal_is_the_launch_check_in_its_words()
    {
        ObjectiveDef objective = TestContent.Objective with { TeamSizeMin = 2, TeamSizeMax = 3 };

        Assert.Equal("A team of 4 is outside the 2 to 3 this objective takes.", LaunchTeam.SizeRefusal(objective, 4));
        Assert.Equal("A team of 1 is outside the 2 to 3 this objective takes.", LaunchTeam.SizeRefusal(objective, 1));
        Assert.Null(LaunchTeam.SizeRefusal(objective, 2));
        Assert.Null(LaunchTeam.SizeRefusal(objective, 3));
    }
}

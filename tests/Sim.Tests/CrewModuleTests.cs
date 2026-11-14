using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;

namespace Sim.Tests;

public class CrewModuleTests
{
    private sealed record EndMission(MissionResult Result) : ICommand;

    private sealed class Harness
    {
        public Harness()
        {
            Bus = new EventBus();
            CycleModule.Register(Bus);
            CrewModule.Register(Bus, TestContent.Pack);
            Bus.Handle<EndMission>((command, context) => context.Publish(new MissionEnded("MISSION_1", "OBJECTIVE_1", command.Result)));
            State = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };
            Bus.Send(State, new FoundLegacy());
        }

        public EventBus Bus { get; }

        public LegacyState State { get; }

        public CrewTuning Tuning => TestContent.Pack.CrewTuning;

        public CrewState Crew => State.Module<CrewState>("crew");

        public IRosterView View => Bus.View<IRosterView>(State);

        public CrewMember Member(string id)
        {
            return Crew.Members.Single(m => m.Id == id);
        }

        public IReadOnlyList<IEvent> End(params CrewResult[] crew)
        {
            return Send(new EndMission(Result(crew))).Select(p => p.Event).ToList();
        }

        public IReadOnlyList<PublishedEvent> Send(ICommand command)
        {
            return Bus.Send(State, command);
        }

        public IReadOnlyList<IEvent> Weeks(int weeks)
        {
            var events = new List<IEvent>();
            int target = State.Week + weeks;
            while (State.Week < target)
            {
                events.AddRange(Send(new AdvancePhase()).Select(p => p.Event));
            }

            return events;
        }
    }

    private static MissionResult Result(CrewResult[] crew)
    {
        return new MissionResult(MissionOutcome.FullSuccess, 1, 1, 0, 0, crew);
    }

    private static CrewResult Gained(string id, double xp, string skill = "SKILL_A")
    {
        return new CrewResult(id, MemberStatus.Active, 0, new Dictionary<string, double> { [skill] = xp }, 0);
    }

    private static CrewResult Hurt(string id, int injury)
    {
        return new CrewResult(id, MemberStatus.Active, injury, new Dictionary<string, double>(), 0);
    }

    private static CrewResult Status(string id, MemberStatus status)
    {
        return new CrewResult(id, status, 0, new Dictionary<string, double>(), 0);
    }

    private static CrewResult Loyal(string id, int loyalty)
    {
        return new CrewResult(id, MemberStatus.Active, 0, new Dictionary<string, double>(), loyalty);
    }

    [Fact]
    public void Founding_creates_the_roster_from_the_pack_at_starting_loyalty()
    {
        var h = new Harness();

        Assert.Equal(8, h.Crew.Members.Count);
        Assert.Equal(TestContent.Pack.Crew.Select(c => c.Id), h.Crew.Members.Select(m => m.Id));
        Assert.All(h.Crew.Members, m =>
        {
            Assert.Equal(50, m.Loyalty);
            Assert.Equal(CrewStatus.Active, m.Status);
            Assert.All(m.Skills.Values, s => Assert.Equal(0, s.Xp));
        });
        Assert.Equal(h.Crew.Members.Select(m => m.Id), h.View.Roster);
        CrewDef def = TestContent.Pack.Crew[0];
        Assert.Equal(def.Skills["SKILL_A"], h.Member(def.Id).Skills["SKILL_A"].Level);
    }

    [Fact]
    public void A_level_is_gained_exactly_at_the_threshold()
    {
        var h = new Harness();
        int level = h.Member("CREW_01").Skills["SKILL_A"].Level;
        int threshold = h.Tuning.XpBase * (level + 1);

        IReadOnlyList<IEvent> below = h.End(Gained("CREW_01", threshold - 1));
        Assert.DoesNotContain(below, e => e is SkillRaised);
        Assert.Equal(level, h.Member("CREW_01").Skills["SKILL_A"].Level);

        IReadOnlyList<IEvent> at = h.End(Gained("CREW_01", 1));

        Assert.Contains(new SkillRaised("CREW_01", "SKILL_A", level + 1), at);
        Assert.Equal(level + 1, h.Member("CREW_01").Skills["SKILL_A"].Level);
        Assert.Equal(0, h.Member("CREW_01").Skills["SKILL_A"].Xp);
    }

    [Fact]
    public void Two_levels_can_be_gained_in_one_mission()
    {
        var h = new Harness();
        int level = h.Member("CREW_01").Skills["SKILL_A"].Level;
        int first = h.Tuning.XpBase * (level + 1);
        int second = h.Tuning.XpBase * (level + 2);

        IReadOnlyList<IEvent> events = h.End(Gained("CREW_01", first + second + 3));

        Assert.Equal(
            new IEvent[] { new SkillRaised("CREW_01", "SKILL_A", level + 1), new SkillRaised("CREW_01", "SKILL_A", level + 2) },
            events.OfType<SkillRaised>());
        Assert.Equal(3, h.Member("CREW_01").Skills["SKILL_A"].Xp);
    }

    [Fact]
    public void No_level_is_gained_past_the_maximum()
    {
        var h = new Harness();
        h.Member("CREW_01").Skills["SKILL_A"].Level = h.Tuning.MaxLevel;

        IReadOnlyList<IEvent> events = h.End(Gained("CREW_01", 1000));

        Assert.DoesNotContain(events, e => e is SkillRaised);
        Assert.Equal(h.Tuning.MaxLevel, h.Member("CREW_01").Skills["SKILL_A"].Level);
        Assert.Equal(0, h.Member("CREW_01").Skills["SKILL_A"].Xp);
    }

    [Fact]
    public void Light_injuries_lower_the_view_skills_and_heal_on_the_due_week_not_before()
    {
        var h = new Harness();
        int level = h.Member("CREW_01").Skills["SKILL_A"].Level;
        int due = h.State.Week + h.Tuning.LightInjuryWeeks;

        IReadOnlyList<IEvent> events = h.End(Hurt("CREW_01", 2));

        Assert.Equal(new[] { due, due }, h.Member("CREW_01").LightInjuriesHealAtWeek);
        Assert.Equal(level - 2, h.View.Member("CREW_01")!.Skills["SKILL_A"]);
        Assert.True(h.View.IsFit("CREW_01"));
        Assert.Contains(new CrewInjured("CREW_01", MemberStatus.Active, 2, null), events);

        h.Weeks(h.Tuning.LightInjuryWeeks - 1);
        Assert.Equal(level - 2, h.View.Member("CREW_01")!.Skills["SKILL_A"]);

        IReadOnlyList<IEvent> healed = h.Weeks(1);
        Assert.Equal(level, h.View.Member("CREW_01")!.Skills["SKILL_A"]);
        Assert.Empty(h.Member("CREW_01").LightInjuriesHealAtWeek);
        Assert.Single(healed.OfType<CrewRecovered>());
    }

    [Fact]
    public void Injuries_never_lower_a_view_skill_below_zero()
    {
        var h = new Harness();

        h.End(Hurt("CREW_01", 50));

        Assert.All(h.View.Member("CREW_01")!.Skills.Values, level => Assert.Equal(0, level));
    }

    [Fact]
    public void Incapacitation_makes_a_member_unfit_until_the_due_week()
    {
        var h = new Harness();
        int due = h.State.Week + h.Tuning.IncapacitatedWeeks;

        IReadOnlyList<IEvent> events = h.End(Status("CREW_02", MemberStatus.Incapacitated));

        Assert.Equal(CrewStatus.Recovering, h.Member("CREW_02").Status);
        Assert.Equal(due, h.Member("CREW_02").ReturnsAtWeek);
        Assert.False(h.View.IsFit("CREW_02"));
        Assert.Contains("CREW_02", h.View.Roster);
        Assert.Contains(new CrewInjured("CREW_02", MemberStatus.Incapacitated, 0, due), events);

        h.Weeks(h.Tuning.IncapacitatedWeeks - 1);
        Assert.False(h.View.IsFit("CREW_02"));

        IReadOnlyList<IEvent> back = h.Weeks(1);
        Assert.True(h.View.IsFit("CREW_02"));
        Assert.Null(h.Member("CREW_02").ReturnsAtWeek);
        Assert.Equal(new[] { new CrewRecovered("CREW_02") }, back.OfType<CrewRecovered>());
    }

    [Fact]
    public void A_member_who_heals_two_things_in_one_week_is_recovered_once()
    {
        var h = new Harness();
        h.End(new CrewResult("CREW_01", MemberStatus.Incapacitated, 1, new Dictionary<string, double>(), 0));
        h.Member("CREW_01").ReturnsAtWeek = h.Member("CREW_01").LightInjuriesHealAtWeek[0];

        IReadOnlyList<IEvent> events = h.Weeks(h.Tuning.LightInjuryWeeks);

        Assert.Single(events.OfType<CrewRecovered>());
        Assert.Equal(CrewStatus.Active, h.Member("CREW_01").Status);
    }

    [Fact]
    public void Death_removes_a_member_from_the_roster_and_view_but_not_the_state()
    {
        var h = new Harness();

        IReadOnlyList<IEvent> events = h.End(Status("CREW_03", MemberStatus.Dead));

        Assert.Equal(CrewStatus.Dead, h.Member("CREW_03").Status);
        Assert.Equal(8, h.Crew.Members.Count);
        Assert.DoesNotContain("CREW_03", h.View.Roster);
        Assert.Null(h.View.Member("CREW_03"));
        Assert.False(h.View.IsFit("CREW_03"));
        Assert.Contains(new CrewDied("CREW_03", "MISSION_1"), events);
        Assert.DoesNotContain(events, e => e is CrewInjured);

        IReadOnlyList<IEvent> later = h.Weeks(20);
        Assert.Equal(CrewStatus.Dead, h.Member("CREW_03").Status);
        Assert.DoesNotContain(later, e => e is CrewRecovered);
    }

    [Fact]
    public void An_unknown_member_has_no_view()
    {
        var h = new Harness();

        Assert.Null(h.View.Member("NOBODY"));
        Assert.False(h.View.IsFit("NOBODY"));
    }

    [Fact]
    public void Loyalty_stops_at_both_bounds()
    {
        var h = new Harness();

        h.End(Loyal("CREW_01", 1000), Loyal("CREW_02", -1000));

        Assert.Equal(h.Tuning.LoyaltyMax, h.Member("CREW_01").Loyalty);
        Assert.Equal(h.Tuning.LoyaltyMin, h.Member("CREW_02").Loyalty);
    }

    [Fact]
    public void Loyalty_moves_by_the_result_times_the_per_point_value()
    {
        var h = new Harness();

        h.End(Loyal("CREW_01", 3));

        Assert.Equal(h.Tuning.StartingLoyalty + 3 * h.Tuning.LoyaltyPerPoint, h.Member("CREW_01").Loyalty);
    }

    [Fact]
    public void Away_missions_count_for_everyone_on_the_team_including_the_dead()
    {
        var h = new Harness();

        h.End(Gained("CREW_01", 0), Status("CREW_02", MemberStatus.Dead));
        h.End(Gained("CREW_01", 0));

        Assert.Equal(2, h.Member("CREW_01").AwayMissions);
        Assert.Equal(1, h.Member("CREW_02").AwayMissions);
        Assert.Equal(0, h.Member("CREW_03").AwayMissions);
    }

    [Fact]
    public void Experience_in_a_skill_the_member_lacks_is_ignored()
    {
        var h = new Harness();

        IReadOnlyList<IEvent> events = h.End(Gained("CREW_01", 1000, "SKILL_NONE"));

        Assert.DoesNotContain(events, e => e is SkillRaised);
        Assert.DoesNotContain("SKILL_NONE", h.Member("CREW_01").Skills.Keys);
    }

    [Fact]
    public void The_state_round_trips_through_SimJson()
    {
        var h = new Harness();
        h.End(Hurt("CREW_01", 1), Status("CREW_02", MemberStatus.Incapacitated), Status("CREW_03", MemberStatus.Dead), Gained("CREW_04", 25));

        string json = JsonSerializer.Serialize(h.Crew, SimJson.Options);
        CrewState back = JsonSerializer.Deserialize<CrewState>(json, SimJson.Options)!;

        Assert.Equal(json, JsonSerializer.Serialize(back, SimJson.Options));
        Assert.Contains("\"Recovering\"", json);
        Assert.Contains("\"Dead\"", json);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CrewState>("""{"Members":[{"Id":"X"}]}""", SimJson.Options));
    }
}

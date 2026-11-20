using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;

namespace Sim.Tests;

public class CrewModuleTests
{
    private sealed record EndMission(MissionResult Result) : ICommand;

    // The parts of a turn the crew module hears, one at a time: the cycle turning (rest), and days passing (everything timed).
    private sealed record TurnCycle : ICommand;

    private sealed record PassDays(int Days) : ICommand;

    private sealed class Harness
    {
        public Harness()
        {
            Bus = new EventBus();
            CycleModule.Register(Bus, TestContent.Pack);
            CrewModule.Register(Bus, TestContent.Pack);
            Bus.Handle<EndMission>((command, context) => context.Publish(new MissionEnded("MISSION_1", "OBJECTIVE_1", command.Result)));
            Bus.Handle<TurnCycle>((_, context) => context.Publish(new CycleAdvanced(++context.State.Cycle)));
            Bus.Handle<PassDays>((command, context) =>
            {
                for (int day = context.State.Day + 1; day <= context.State.Day + command.Days; day++)
                {
                    context.Publish(new DayReached(day));
                }
            });
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

        public IReadOnlyList<IEvent> End(MissionOutcome outcome, params CrewResult[] crew)
        {
            return Send(new EndMission(Result(crew) with { Outcome = outcome })).Select(p => p.Event).ToList();
        }

        public IReadOnlyList<PublishedEvent> Send(ICommand command)
        {
            return Bus.Send(State, command);
        }

        // A week's days, in the fixture's calendar.
        public static int Days(int weeks) => Calendar.Days(TestContent.Pack.Calendar, weeks);

        // The cycle turns, with no day passing: rest, alone.
        public IReadOnlyList<IEvent> Turn()
        {
            return Send(new TurnCycle()).Select(p => p.Event).ToList();
        }

        public IReadOnlyList<IEvent> PassDays(int days)
        {
            return Send(new PassDays(days)).Select(p => p.Event).ToList();
        }

        // A cycle's rest and then a week of days: drift once, on the week's last day.
        public IReadOnlyList<IEvent> Week()
        {
            return Turn().Concat(PassDays(Days(1))).ToList();
        }
    }

    private static MissionResult Result(CrewResult[] crew)
    {
        return new MissionResult(MissionOutcome.FullSuccess, 1, 1, 0, 0, crew, []);
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

    // The order must not depend on how the result's dictionary happens to enumerate.
    [Fact]
    public void Skills_are_raised_in_ordinal_order_of_skill_id()
    {
        var h = new Harness();
        var experience = new Dictionary<string, double> { ["SKILL_C"] = 1000, ["SKILL_A"] = 1000, ["SKILL_B"] = 1000 };

        IReadOnlyList<IEvent> events = h.End(new CrewResult("CREW_01", MemberStatus.Active, 0, experience, 0));

        string[] raised = events.OfType<SkillRaised>().Select(e => e.SkillId).ToArray();
        Assert.Equal(["SKILL_A", "SKILL_B", "SKILL_C"], raised.Distinct());
        Assert.Equal(raised.Order(StringComparer.Ordinal), raised);
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
    public void Light_injuries_lower_the_view_skills_and_heal_on_the_due_day_not_before()
    {
        var h = new Harness();
        int level = h.Member("CREW_01").Skills["SKILL_A"].Level;
        int due = h.State.Day + Harness.Days(h.Tuning.LightInjuryWeeks);

        IReadOnlyList<IEvent> events = h.End(Hurt("CREW_01", 2));

        Assert.Equal(new[] { due, due }, h.Member("CREW_01").LightInjuriesHealAtDay);
        Assert.Equal(level - 2, h.View.Member("CREW_01")!.Skills["SKILL_A"]);
        Assert.True(h.View.IsFit("CREW_01"));
        Assert.Contains(new CrewInjured("CREW_01", MemberStatus.Active, 2, null), events);

        h.PassDays(Harness.Days(h.Tuning.LightInjuryWeeks) - 1);
        Assert.Equal(level - 2, h.View.Member("CREW_01")!.Skills["SKILL_A"]);

        IReadOnlyList<IEvent> healed = h.PassDays(1);
        Assert.Equal(level, h.View.Member("CREW_01")!.Skills["SKILL_A"]);
        Assert.Empty(h.Member("CREW_01").LightInjuriesHealAtDay);
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
    public void Incapacitation_makes_a_member_unfit_until_the_due_day()
    {
        var h = new Harness();
        int due = h.State.Day + Harness.Days(h.Tuning.IncapacitatedWeeks);

        IReadOnlyList<IEvent> events = h.End(Status("CREW_02", MemberStatus.Incapacitated));

        Assert.Equal(CrewStatus.Recovering, h.Member("CREW_02").Status);
        Assert.Equal(due, h.Member("CREW_02").ReturnsAtDay);
        Assert.False(h.View.IsFit("CREW_02"));
        Assert.Contains("CREW_02", h.View.Roster);
        Assert.Contains(new CrewInjured("CREW_02", MemberStatus.Incapacitated, 0, due), events);

        h.PassDays(Harness.Days(h.Tuning.IncapacitatedWeeks) - 1);
        Assert.False(h.View.IsFit("CREW_02"));

        IReadOnlyList<IEvent> back = h.PassDays(1);
        Assert.True(h.View.IsFit("CREW_02"));
        Assert.Null(h.Member("CREW_02").ReturnsAtDay);
        Assert.Equal(new[] { new CrewRecovered("CREW_02") }, back.OfType<CrewRecovered>());
    }

    [Fact]
    public void A_member_who_heals_two_things_on_one_day_is_recovered_once()
    {
        var h = new Harness();
        h.End(new CrewResult("CREW_01", MemberStatus.Incapacitated, 1, new Dictionary<string, double>(), 0));
        h.Member("CREW_01").ReturnsAtDay = h.Member("CREW_01").LightInjuriesHealAtDay[0];

        IReadOnlyList<IEvent> events = h.PassDays(Harness.Days(h.Tuning.LightInjuryWeeks));

        Assert.Single(events.OfType<CrewRecovered>());
        Assert.Equal(CrewStatus.Active, h.Member("CREW_01").Status);
    }

    [Fact]
    public void Death_removes_a_member_from_the_roster_and_view_but_not_the_state()
    {
        var h = new Harness();

        IReadOnlyList<IEvent> events = h.End(Status("CREW_03", MemberStatus.Dead) with { DiedAtEncounter = "ENC_01", DiedAtApproach = "careful" });

        Assert.Equal(CrewStatus.Dead, h.Member("CREW_03").Status);
        Assert.Equal(8, h.Crew.Members.Count);
        Assert.DoesNotContain("CREW_03", h.View.Roster);
        Assert.Null(h.View.Member("CREW_03"));
        Assert.False(h.View.IsFit("CREW_03"));
        Assert.Contains(new CrewDied("CREW_03", "MISSION_1", "ENC_01", "careful"), events);
        Assert.DoesNotContain(events, e => e is CrewInjured);

        IReadOnlyList<IEvent> later = h.PassDays(Harness.Days(20));
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

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Members":null}""")]
    public void A_stored_state_without_a_roster_is_refused(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CrewState>(json, SimJson.Options));
    }

    [Fact]
    public void A_fresh_state_round_trips_and_the_bus_creates_it_empty()
    {
        string json = JsonSerializer.Serialize(new CrewState(), SimJson.Options);

        Assert.Empty(JsonSerializer.Deserialize<CrewState>(json, SimJson.Options)!.Members);

        var bus = new EventBus();
        CrewModule.Register(bus, TestContent.Pack);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };
        Assert.Empty(bus.View<IRosterView>(state).Roster);
    }

    [Fact]
    public void An_unknown_crew_id_in_a_result_is_a_bug_and_throws()
    {
        var h = new Harness();

        var ex = Assert.Throws<InvalidOperationException>(() => h.End(Gained("NOBODY", 1)));

        Assert.Contains("NOBODY", ex.Message);
    }

    [Fact]
    public void A_member_who_dies_gets_no_light_injury_entries()
    {
        var h = new Harness();

        h.End(new CrewResult("CREW_01", MemberStatus.Dead, 2, new Dictionary<string, double>(), 0));

        Assert.Empty(h.Member("CREW_01").LightInjuriesHealAtDay);
    }

    [Fact]
    public void The_view_before_founding_is_empty_and_stores_nothing()
    {
        var bus = new EventBus();
        CycleModule.Register(bus, TestContent.Pack);
        CrewModule.Register(bus, TestContent.Pack);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };

        Assert.Empty(bus.View<IRosterView>(state).Roster);
        Assert.Empty(state.Modules);

        bus.Send(state, new FoundLegacy());
        Assert.Equal(8, bus.View<IRosterView>(state).Roster.Count);
    }

    [Fact]
    public void Light_injuries_from_missions_in_different_weeks_heal_on_their_own_days()
    {
        var h = new Harness();
        int level = h.Member("CREW_01").Skills["SKILL_A"].Level;
        h.End(Hurt("CREW_01", 1));
        h.PassDays(Harness.Days(1));
        h.End(Hurt("CREW_01", 1));
        Assert.Equal(level - 2, h.View.Member("CREW_01")!.Skills["SKILL_A"]);

        h.PassDays(Harness.Days(h.Tuning.LightInjuryWeeks - 1));
        Assert.Equal(level - 1, h.View.Member("CREW_01")!.Skills["SKILL_A"]);

        h.PassDays(Harness.Days(1));
        Assert.Equal(level, h.View.Member("CREW_01")!.Skills["SKILL_A"]);
    }

    [Fact]
    public void An_incapacitated_and_lightly_injured_member_is_reported_as_incapacitated_with_both_counts()
    {
        var h = new Harness();
        int due = h.State.Day + Harness.Days(h.Tuning.IncapacitatedWeeks);

        IReadOnlyList<IEvent> events = h.End(new CrewResult("CREW_01", MemberStatus.Incapacitated, 2, new Dictionary<string, double>(), 0));

        Assert.Equal(new[] { new CrewInjured("CREW_01", MemberStatus.Incapacitated, 2, due) }, events.OfType<CrewInjured>());
    }

    // Morale (1e part two, design 4.1). The fixture's tuning: resting 50, low 25, high 75, drift 1, rested 2, hurt -4,
    // incapacitated -10, crewmateDied -8, and a full success +6, a loss -6.
    [Fact]
    public void Morale_starts_at_resting()
    {
        var h = new Harness();

        Assert.All(h.Crew.Members, m => Assert.Equal(h.Tuning.MoraleResting, m.Morale));
        Assert.All(h.Crew.Members, m => Assert.Equal(0, h.View.Member(m.Id)!.MoraleBonus));
    }

    [Fact]
    public void A_mission_result_moves_morale_for_those_who_came_home()
    {
        var h = new Harness();

        IReadOnlyList<IEvent> won = h.End(Gained("CREW_01", 0));
        IReadOnlyList<IEvent> lost = h.End(MissionOutcome.Loss, Gained("CREW_02", 0));

        Assert.Equal(new[] { new MoraleChanged("CREW_01", 50, 56, MoraleCauses.Mission) }, won.OfType<MoraleChanged>());
        Assert.Equal(new[] { new MoraleChanged("CREW_02", 50, 44, MoraleCauses.Mission) }, lost.OfType<MoraleChanged>());
        Assert.Equal(50, h.Member("CREW_03").Morale);
    }

    [Fact]
    public void An_injury_and_an_incapacitation_lower_morale()
    {
        var h = new Harness();

        IReadOnlyList<IEvent> events = h.End(Hurt("CREW_01", 2), new CrewResult("CREW_02", MemberStatus.Incapacitated, 1, new Dictionary<string, double>(), 0));

        Assert.Equal(
            new[]
            {
                new MoraleChanged("CREW_01", 50, 56, MoraleCauses.Mission), new MoraleChanged("CREW_01", 56, 48, MoraleCauses.Hurt),
                new MoraleChanged("CREW_02", 50, 56, MoraleCauses.Mission), new MoraleChanged("CREW_02", 56, 46, MoraleCauses.Hurt),
            },
            events.OfType<MoraleChanged>());
    }

    // Review focus 1: the dead get nothing; every living member, on the team or not, feels each death once.
    [Fact]
    public void A_death_lowers_every_living_members_morale_and_not_the_deads()
    {
        var h = new Harness();

        IReadOnlyList<IEvent> events = h.End(Status("CREW_01", MemberStatus.Dead), Status("CREW_02", MemberStatus.Dead), Gained("CREW_03", 0));

        Assert.DoesNotContain(events.OfType<MoraleChanged>(), e => e.CrewId is "CREW_01" or "CREW_02");
        Assert.Equal(50, h.Member("CREW_01").Morale);
        Assert.Equal(50 + 6 - 8 - 8, h.Member("CREW_03").Morale);
        Assert.Equal(50 - 8 - 8, h.Member("CREW_04").Morale);
        Assert.Equal(2, events.OfType<MoraleChanged>().Count(e => e.CrewId == "CREW_04" && e.Cause == MoraleCauses.Death));
    }

    [Fact]
    public void A_member_not_sent_rests_and_one_sent_does_not()
    {
        var h = new Harness();
        h.Member("CREW_01").Morale = 40;
        h.Member("CREW_02").Morale = 40;
        h.End(MissionOutcome.Withdrawal, Gained("CREW_01", 0));

        IReadOnlyList<IEvent> events = h.Week();

        Assert.Equal(38 + 1, h.Member("CREW_01").Morale);
        Assert.Equal(40 + 2 + 1, h.Member("CREW_02").Morale);
        Assert.Contains(new MoraleChanged("CREW_02", 40, 42, MoraleCauses.Rest), events);
        Assert.DoesNotContain(events.OfType<MoraleChanged>(), e => e.CrewId == "CREW_01" && e.Cause == MoraleCauses.Rest);
    }

    [Fact]
    public void A_member_recovering_does_not_rest()
    {
        var h = new Harness();
        h.End(MissionOutcome.Withdrawal, Status("CREW_01", MemberStatus.Incapacitated));
        h.Week();
        int before = h.Member("CREW_01").Morale;

        IReadOnlyList<IEvent> events = h.Week();

        Assert.Equal(before + 1, h.Member("CREW_01").Morale);
        Assert.DoesNotContain(events.OfType<MoraleChanged>(), e => e.CrewId == "CREW_01" && e.Cause == MoraleCauses.Rest);
    }

    // Review focus 2.
    [Theory]
    [InlineData(51, 50)]
    [InlineData(60, 59)]
    [InlineData(49, 50)]
    [InlineData(45, 48)]
    [InlineData(50, 50)]
    public void Rest_and_drift_move_towards_resting_and_never_past_it(int from, int to)
    {
        var h = new Harness();
        h.Member("CREW_01").Morale = from;

        h.Week();

        Assert.Equal(to, h.Member("CREW_01").Morale);
    }

    // Every week from the legacy's start day (design 4.5), and on no day between.
    [Fact]
    public void Morale_drifts_on_every_week_from_the_start_day_and_not_between()
    {
        var h = new Harness();
        h.Member("CREW_01").Morale = 40;

        h.PassDays(Harness.Days(1) - 1);
        Assert.Equal(40, h.Member("CREW_01").Morale);

        IReadOnlyList<IEvent> first = h.PassDays(1);
        Assert.Equal(new[] { new MoraleChanged("CREW_01", 40, 41, MoraleCauses.Drift) }, first.OfType<MoraleChanged>().Where(e => e.CrewId == "CREW_01"));

        h.PassDays(Harness.Days(3));
        Assert.Equal(44, h.Member("CREW_01").Morale);
    }

    // Rest is per cycle, not per day: on a real turn it comes before the first day, and a turn of several days rests once.
    [Fact]
    public void A_turn_rests_once_before_its_first_day()
    {
        var h = new Harness();
        h.Member("CREW_02").Morale = 40;
        h.State.Phase = CyclePhase.Advance;

        List<IEvent> events = h.Send(new Travel(3)).Select(p => p.Event).ToList();

        int rest = events.FindIndex(e => e is MoraleChanged { Cause: MoraleCauses.Rest, CrewId: "CREW_02" });
        Assert.True(rest >= 0 && rest < events.FindIndex(e => e is DayReached));
        Assert.Single(events.OfType<MoraleChanged>(), e => e.Cause == MoraleCauses.Rest && e.CrewId == "CREW_02");
    }

    [Fact]
    public void Morale_is_clamped_and_an_answer_is_its_cause()
    {
        var h = new Harness();

        IReadOnlyList<IEvent> up = h.Send(new AdjustMorale("CREW_01", 500)).Select(p => p.Event).ToList();
        h.Send(new AdjustMorale("CREW_02", -500));

        Assert.Equal(new[] { new MoraleChanged("CREW_01", 50, h.Tuning.MoraleMax, MoraleCauses.Answer) }, up.OfType<MoraleChanged>());
        Assert.Equal(h.Tuning.MoraleMin, h.Member("CREW_02").Morale);
        Assert.Empty(h.Send(new AdjustMorale("CREW_01", 1)).Select(p => p.Event).OfType<MoraleChanged>());
    }

    [Fact]
    public void AdjustMorale_is_refused_for_the_dead_and_the_unknown()
    {
        var h = new Harness();
        h.End(Status("CREW_01", MemberStatus.Dead));

        Assert.Throws<CommandRejectedException>(() => h.Send(new AdjustMorale("CREW_01", 1)));
        Assert.Throws<CommandRejectedException>(() => h.Send(new AdjustMorale("CREW_99", 1)));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(25, -1)]
    [InlineData(26, 0)]
    [InlineData(74, 0)]
    [InlineData(75, 1)]
    [InlineData(100, 1)]
    public void The_roster_view_gives_the_bonus_at_the_thresholds(int morale, int bonus)
    {
        var h = new Harness();
        h.Member("CREW_01").Morale = morale;

        Assert.Equal(bonus, h.View.Member("CREW_01")!.MoraleBonus);
    }
}

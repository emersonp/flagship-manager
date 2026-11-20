using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;
using Sim.Traits;
using static Sim.Tests.StoryletsModuleTests;

namespace Sim.Tests;

// The traits module (slice 1e, part three): storage, changes, and Dev Play's sets. These were the storylets module's flag
// tests until Task 1 of part three carved the module out; the harness is the storylets module's, which builds both.
public class TraitsModuleTests
{
    [Fact]
    public void A_missions_trait_is_stored_with_the_day_the_cycle_and_the_encounter()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Advance);
        h.To(CyclePhase.Resolution);

        List<IEvent> events = h.End([Raise("trait_a", 2), Fix("trait_b", 3, "CREW_02", "ENCOUNTER_04")]);

        int day = h.State.Day;
        Assert.Equal(2, h.State.Cycle);
        Assert.Equal(
            [new TraitChanged("trait_a", null, null, 0, 2, TraitCauses.Mission, Encounter), new TraitChanged("trait_b", "CREW_02", null, 0, 3, TraitCauses.Mission, "ENCOUNTER_04")],
            events.OfType<TraitChanged>());
        Assert.Equal(
            [("trait_a", null, 2, day, 2, Encounter), ("trait_b", "CREW_02", 3, day, 2, "ENCOUNTER_04")],
            h.Traits.Traits.Select(t => (t.Trait, t.CrewId, t.Value, t.ChangedAtDay, t.ChangedAtCycle, t.EncounterId)));

        // The state is stored as JSON with the legacy, so it must read back whole.
        string json = JsonSerializer.Serialize(h.Traits, SimJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<TraitsState>(json, SimJson.Options), SimJson.Options));
    }

    [Fact]
    public void Add_and_set_never_go_below_zero_and_a_trait_at_zero_is_gone()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Resolution);

        h.End([Raise("trait_a", 2), Fix("trait_b", 3)]);
        List<IEvent> events = h.End([Raise("trait_a", -5), Fix("trait_b", 0), Raise("trait_c", -1), Fix("trait_d", 0)]);

        Assert.Equal(
            [new TraitChanged("trait_a", null, null, 2, 0, TraitCauses.Mission, Encounter), new TraitChanged("trait_b", null, null, 3, 0, TraitCauses.Mission, Encounter)],
            events.OfType<TraitChanged>());
        Assert.Empty(h.Traits.Traits);

        h.End([Raise("trait_a", 1), Raise("trait_a", 1), Fix("trait_a", 1)]);

        Assert.Equal(1, h.Trait("trait_a"));
        Assert.Single(h.Traits.Traits);
    }

    [Fact]
    public void A_trait_on_someone_who_died_on_that_mission_is_not_stored()
    {
        var h = new Harness(Pack(2, Shaken));
        h.To(CyclePhase.Resolution);

        List<IEvent> events = h.End([Raise("trait_b", 1, "CREW_02"), Raise("trait_b", 1, "CREW_03")], Died("CREW_02"));
        events.AddRange(h.To(CyclePhase.Itinerary));

        Assert.Contains(new CrewDied("CREW_02", "MISSION_1", null, null), events);
        Assert.Equal([new TraitChanged("trait_b", "CREW_03", null, 0, 1, TraitCauses.Mission, Encounter)], events.OfType<TraitChanged>());
        Assert.Equal(["CREW_03"], h.Traits.Traits.Select(t => t.CrewId));
        Assert.Equal(["CREW_03"], events.OfType<StoryletOffered>().Select(o => o.Cast["subject"]));
    }

    [Fact]
    public void An_answers_and_a_vignettes_changes_carry_their_cause()
    {
        var glance = new VignetteDef("VIGNETTE_GLANCE", [], new Expr("ship.trait.trait_a >= 1"), [Add("trait_c", 1, "ship")], [], null);
        var h = new Harness(Pack(2, Quirk) with { Vignettes = [glance] });

        List<IEvent> arrived = h.Mission(Raise("trait_a", 1));
        List<IEvent> answered = h.Send(new AnswerStorylet("2:STORYLET_QUIRK", "ride"));

        Assert.Equal([new TraitChanged("trait_c", null, null, 0, 1, TraitCauses.Vignette, null)], arrived.OfType<TraitChanged>().Where(t => t.Trait == "trait_c"));
        Assert.Equal([new TraitChanged("trait_a", null, null, 1, 2, TraitCauses.Answer, null)], answered.OfType<TraitChanged>());
    }

    [Fact]
    public void A_developers_trait_is_its_own_event_keeps_where_the_trait_came_from_and_brings_its_storylet_at_the_next_itinerary()
    {
        var h = new Harness(Pack(2, Quirk, Shaken));
        h.Mission(Raise("trait_a", 1));
        h.Send(new AnswerStorylet("2:STORYLET_QUIRK", "clear"));
        h.To(CyclePhase.Orders);

        List<IEvent> ship = h.Send(new SetTrait("trait_a", null, null, 1));
        List<IEvent> crew = h.Send(new SetTrait("trait_b", "CREW_03", null, 2));

        Assert.Equal([new TraitSetByDeveloper("trait_a", null, null, 1), new TraitChanged("trait_a", null, null, 0, 1, TraitCauses.Developer, null)], ship);
        Assert.Equal([new TraitSetByDeveloper("trait_b", "CREW_03", null, 2), new TraitChanged("trait_b", "CREW_03", null, 0, 2, TraitCauses.Developer, null)], crew);
        Assert.Empty(h.Waiting);

        List<IEvent> itinerary = h.To(CyclePhase.Itinerary);

        Assert.Equal(["3:STORYLET_SHAKEN", "3:STORYLET_QUIRK"], itinerary.OfType<StoryletOffered>().Select(o => o.OfferId));
        h.Send(new SetTrait("trait_b", "CREW_03", null, 0));
        Assert.Equal(0, h.Trait("trait_b", "CREW_03"));
    }

    [Theory]
    [InlineData("Trait_A", null, 1, "Trait id 'Trait_A' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("", null, 1, "Trait id '' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("trait_a", null, -1, "A trait's value is 0 to 99.")]
    [InlineData("trait_a", null, 100, "A trait's value is 0 to 99.")]
    [InlineData("trait_a", "CREW_99", 1, "Crew member 'CREW_99' is not on the roster.")]
    [InlineData("trait_a", null, 0, "trait_a is already 0 on the ship.")]
    [InlineData("trait_b", "CREW_02", 0, "trait_b is already 0 on CREW_02.")]
    public void A_developers_trait_is_refused_with_the_reason(string trait, string? crewId, int value, string reason)
    {
        var h = new Harness(Pack(2));

        Assert.Equal(reason, h.Refused(new SetTrait(trait, crewId, null, value)));
        Assert.Empty(h.Traits.Traits);
    }

    // --- Definitions (1e part three, Task 2) ---

    private static TraitDef Def(string id, int? max = null, TraitStep? every = null, int? expires = null, Dictionary<int, string>? levels = null)
    {
        return TraitDef.Plain(id) with { Max = max, Every = every, ExpiresWeeks = expires, Levels = levels ?? new Dictionary<int, string>() };
    }

    private static Harness With(params TraitDef[] defs)
    {
        return new Harness(Pack(2) with { Traits = defs });
    }

    // One week of days, with no turn of the cycle: the events of the days.
    private static List<IEvent> Week(Harness h)
    {
        return h.PassDays(Calendar.Days(TestContent.Pack.Calendar, 1));
    }

    private static List<TraitChanged> Changes(List<IEvent> events)
    {
        return events.OfType<TraitChanged>().ToList();
    }

    [Fact]
    public void A_change_is_clamped_to_max()
    {
        Harness h = With(Def("trait_x", max: 3));
        h.To(CyclePhase.Resolution);

        List<IEvent> events = h.End([Raise("trait_x", 5)]);
        events.AddRange(h.Send(new ChangeTrait("trait_x", null, null, 2, false, TraitCauses.Answer)));

        Assert.Equal([new TraitChanged("trait_x", null, null, 0, 3, TraitCauses.Mission, Encounter)], Changes(events));
        Assert.Equal(3, h.Trait("trait_x"));
        Assert.Equal("trait_x is at most 3.", h.Refused(new SetTrait("trait_x", null, null, 4)));
    }

    [Fact]
    public void A_trait_with_levels_cannot_be_added_to()
    {
        Harness h = With(Def("trait_x", levels: new Dictionary<int, string> { [1] = "level_a", [2] = "level_b" }));

        Assert.Equal("trait_x has named levels, so it is set, not added to.", h.Refused(new ChangeTrait("trait_x", "CREW_01", null, 1, false, TraitCauses.Answer)));
        Assert.Equal("trait_x is set to 0 or one of its levels: level_a (1), level_b (2).", h.Refused(new SetTrait("trait_x", "CREW_01", null, 3)));
        Assert.Equal([new TraitChanged("trait_x", "CREW_01", null, 0, 2, TraitCauses.Answer, null)], Changes(h.Send(new ChangeTrait("trait_x", "CREW_01", null, 2, true, TraitCauses.Answer))));
    }

    [Fact]
    public void Every_steps_down_after_the_weeks_and_restarts_its_clock()
    {
        Harness h = With(Def("trait_x", every: new TraitStep(2, -1)));
        h.To(CyclePhase.Resolution);
        h.End([Fix("trait_x", 3)]);
        int set = h.State.Day;

        Assert.Empty(Changes(Week(h)));
        Assert.Equal([new TraitChanged("trait_x", null, null, 3, 2, TraitCauses.Weekly, null)], Changes(Week(h)));
        Assert.Equal(set + Calendar.Days(TestContent.Pack.Calendar, 2), h.Traits.Traits.Single().ChangedAtDay);
        Assert.Empty(Changes(Week(h)));
        Assert.Equal([new TraitChanged("trait_x", null, null, 2, 1, TraitCauses.Weekly, null)], Changes(Week(h)));
        Week(h);
        Assert.Equal([new TraitChanged("trait_x", null, null, 1, 0, TraitCauses.Weekly, null)], Changes(Week(h)));
        Assert.Empty(h.Traits.Traits);
    }

    [Fact]
    public void A_rising_step_stops_at_max()
    {
        Harness h = With(Def("trait_x", max: 2, every: new TraitStep(1, 1)));
        h.Send(new ChangeTrait("trait_x", "CREW_02", null, 1, true, TraitCauses.Answer));

        Assert.Equal([new TraitChanged("trait_x", "CREW_02", null, 1, 2, TraitCauses.Weekly, null)], Changes(Week(h)));
        Assert.Empty(Changes(Week(h)));
        Assert.Empty(Changes(Week(h)));
        Assert.Equal(2, h.Trait("trait_x", "CREW_02"));
    }

    [Fact]
    public void Expires_sets_to_zero_after_the_weeks_unchanged()
    {
        Harness h = With(Def("trait_x", expires: 2));
        h.Send(new ChangeTrait("trait_x", "CREW_02", null, 4, true, TraitCauses.Answer));

        Assert.Empty(Changes(Week(h)));
        Assert.Equal([new TraitChanged("trait_x", "CREW_02", null, 4, 0, TraitCauses.Expired, null)], Changes(Week(h)));
        Assert.Empty(h.Traits.Traits);
    }

    // Both clocks count from the last change, by anything: an answer in week 1 puts off the step to week 3 and the expiry to week 4,
    // and the step in week 3 is itself a change, so the expiry moves again.
    [Fact]
    public void A_change_restarts_both_clocks()
    {
        Harness h = With(Def("trait_x", every: new TraitStep(2, -1), expires: 3));
        h.Send(new ChangeTrait("trait_x", null, null, 3, true, TraitCauses.Answer));
        Week(h);
        h.Send(new ChangeTrait("trait_x", null, null, 1, false, TraitCauses.Answer));

        Assert.Empty(Changes(Week(h)));
        Assert.Equal([new TraitChanged("trait_x", null, null, 4, 3, TraitCauses.Weekly, null)], Changes(Week(h)));
        Assert.Empty(Changes(Week(h)));
        Assert.Equal([new TraitChanged("trait_x", null, null, 3, 2, TraitCauses.Weekly, null)], Changes(Week(h)));
    }

    // Date order across modules (design 5.4, review focus 1): on one day the crew module runs before the traits module, and on a
    // span of days nothing due on a later day happens before something due on an earlier one.
    [Fact]
    public void On_one_day_an_injury_heals_before_a_trait_expires()
    {
        int weeks = TestContent.Pack.CrewTuning.LightInjuryWeeks;
        var h = new Harness(Pack(2) with { Traits = [Def("trait_x", expires: weeks)] });
        h.To(CyclePhase.Resolution);
        h.End([Fix("trait_x", 1)], new CrewResult("CREW_01", MemberStatus.Active, 1, new Dictionary<string, double>(), 0));
        int due = h.State.Day + Calendar.Days(TestContent.Pack.Calendar, weeks);
        Assert.Equal(due, h.Crew.Members.Single(m => m.Id == "CREW_01").LightInjuriesHealAtDay.Single());
        h.To(CyclePhase.Advance);

        List<IEvent> events = h.Send(new Travel(weeks + 1));

        int day = events.FindIndex(e => e is DayReached { Day: var d } && d == due);
        int healed = events.FindIndex(e => e is CrewRecovered);
        int expired = events.FindIndex(e => e is TraitChanged { Cause: TraitCauses.Expired });
        int next = events.FindIndex(e => e is DayReached { Day: var d } && d == due + 1);
        Assert.True(day < healed && healed < expired && expired < next, $"{day}, {healed}, {expired}, {next}");
    }

    [Fact]
    public void A_trait_that_expires_early_in_a_journey_never_takes_the_step_due_later_in_it()
    {
        var h = new Harness(Pack(2) with { Traits = [Def("trait_x", every: new TraitStep(2, 1), expires: 1)] });
        h.Send(new ChangeTrait("trait_x", null, null, 1, true, TraitCauses.Answer));
        h.To(CyclePhase.Advance);
        // Changed 3 days short of a week ago: it expires on the journey's 3rd day; its step would fall on the 9th.
        h.Traits.Traits.Single().ChangedAtDay = h.State.Day - 3;

        List<IEvent> events = h.Send(new Travel(5));

        Assert.Equal([new TraitChanged("trait_x", null, null, 1, 0, TraitCauses.Expired, null)], Changes(events));
        int expired = events.FindIndex(e => e is TraitChanged);
        Assert.Equal(h.State.Day - events.OfType<CycleLengthRolled>().Single().Days + 3, events.Take(expired).OfType<DayReached>().Last().Day);
    }

    [Fact]
    public void Nothing_is_published_when_nothing_changes()
    {
        Harness h = With(Def("trait_x", max: 2));
        h.Send(new ChangeTrait("trait_x", null, null, 2, true, TraitCauses.Answer));
        int day = h.Traits.Traits.Single().ChangedAtDay;
        Week(h);

        Assert.Empty(h.Send(new ChangeTrait("trait_x", null, null, 2, true, TraitCauses.Answer)));
        Assert.Empty(h.Send(new ChangeTrait("trait_x", null, null, 1, false, TraitCauses.Answer)));
        Assert.Empty(h.Send(new ChangeTrait("trait_y", null, null, 0, true, TraitCauses.Answer)));
        Assert.Empty(h.Send(new ChangeTrait("trait_y", null, null, -1, false, TraitCauses.Answer)));
        Assert.Equal(day, h.Traits.Traits.Single().ChangedAtDay);
    }

    // --- Pairs (1e part three, Task 3): the fixture's trait_g avoids, trait_h owes, and trait_j is the grief trait_h leaves ---

    private static MissionTrait Toward(string trait, int amount, string holderId, string targetId)
    {
        return new MissionTrait(trait, amount, false, holderId, Encounter, targetId);
    }

    [Fact]
    public void A_pair_is_stored_with_its_target()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Resolution);

        List<IEvent> events = h.End([Toward("trait_g", 2, "CREW_02", "CREW_03")]);

        Assert.Equal([new TraitChanged("trait_g", "CREW_02", "CREW_03", 0, 2, TraitCauses.Mission, Encounter)], Changes(events));
        Assert.Equal((2, 0, 0), (h.Trait("trait_g", "CREW_02", "CREW_03"), h.Trait("trait_g", "CREW_02"), h.Trait("trait_g", "CREW_03", "CREW_02")));
    }

    [Fact]
    public void A_pair_whose_target_or_holder_died_on_the_mission_is_not_stored()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Resolution);

        List<IEvent> events = h.End([Toward("trait_g", 1, "CREW_02", "CREW_03"), Toward("trait_g", 1, "CREW_03", "CREW_04")], Died("CREW_03"));

        Assert.Empty(Changes(events));
        Assert.Empty(h.Traits.Traits);
    }

    // trait_h's onTargetDied: morale -10 and trait_j +1 for the holder; trait_g's: set 0. Each runs once, then every trait on the dead or
    // pointing at them goes, with cause death.
    [Fact]
    public void The_targets_death_runs_onTargetDied_then_drops_the_pair()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Resolution);
        h.End([Toward("trait_h", 1, "CREW_02", "CREW_03"), Toward("trait_g", 2, "CREW_04", "CREW_03"), Raise("trait_b", 1, "CREW_03")]);
        int morale = h.Crew.Members.Single(m => m.Id == "CREW_02").Morale;

        List<IEvent> events = h.End([], Died("CREW_03"));

        Assert.Equal(
            [
                new TraitChanged("trait_j", "CREW_02", null, 0, 1, TraitCauses.Death, null),
                new TraitChanged("trait_g", "CREW_04", "CREW_03", 2, 0, TraitCauses.Death, null),
                new TraitChanged("trait_h", "CREW_02", "CREW_03", 1, 0, TraitCauses.Death, null),
                new TraitChanged("trait_b", "CREW_03", null, 1, 0, TraitCauses.Death, null),
            ],
            Changes(events));
        MoraleChanged grief = Assert.Single(events.OfType<MoraleChanged>(), m => m.Cause == MoraleCauses.Trait);
        Assert.Equal(("CREW_02", -10), (grief.CrewId, grief.To - grief.From));
        Assert.True(grief.From <= morale);
        Assert.Equal([("trait_j", "CREW_02")], h.Traits.Traits.Select(t => (t.Trait, t.CrewId)));
    }

    [Fact]
    public void The_holders_death_drops_the_pair_with_no_rule()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Resolution);
        h.End([Toward("trait_h", 1, "CREW_02", "CREW_03")]);

        List<IEvent> events = h.End([], Died("CREW_02"));

        Assert.Equal([new TraitChanged("trait_h", "CREW_02", "CREW_03", 1, 0, TraitCauses.Death, null)], Changes(events));
        Assert.DoesNotContain(events, e => e is MoraleChanged { Cause: MoraleCauses.Trait });
        Assert.Empty(h.Traits.Traits);
    }

    // --- Launch rules and bonuses (1e part three, Task 4): the fixture's trait_e is the stood_down shape, trait_g the avoids shape ---

    private static List<IEvent> SetUp(Harness h, string trait, string holder, int value, string? target = null)
    {
        return h.Send(new ChangeTrait(trait, holder, target, value, true, TraitCauses.Answer));
    }

    [Fact]
    public void Sent_and_notSent_fire_at_launch()
    {
        var h = new Harness(Pack(2));
        SetUp(h, "trait_e", "CREW_01", 1);
        SetUp(h, "trait_e", "CREW_04", 1);
        int loyalty = h.Crew.Members.Single(m => m.Id == "CREW_01").Loyalty;

        List<IEvent> events = h.Launch("CREW_01", "CREW_02");

        Assert.Equal(
            [new TraitChanged("trait_e", "CREW_01", null, 1, 2, TraitCauses.Launch, null), new TraitChanged("trait_e", "CREW_04", null, 1, 0, TraitCauses.Launch, null)],
            Changes(events));
        Assert.Equal(loyalty - 2, h.Crew.Members.Single(m => m.Id == "CREW_01").Loyalty);
        MoraleChanged morale = Assert.Single(events.OfType<MoraleChanged>());
        Assert.Equal(("CREW_01", -4, MoraleCauses.Trait), (morale.CrewId, morale.To - morale.From, morale.Cause));
    }

    [Fact]
    public void NotSent_does_not_fire_when_nobody_is_sent()
    {
        var h = new Harness(Pack(2));
        SetUp(h, "trait_e", "CREW_04", 1);
        ITraitView view = h.Bus.View<ITraitView>(h.State);

        List<IEvent> week = h.To(CyclePhase.Itinerary);

        Assert.Empty(Changes(week));
        Assert.Equal(1, h.Trait("trait_e", "CREW_04"));
        Assert.All(view.Preview(["CREW_04"]), p => Assert.False(p.Fires([])));
    }

    [Fact]
    public void WithTarget_fires_when_both_are_sent()
    {
        var h = new Harness(Pack(2));
        SetUp(h, "trait_g", "CREW_02", 2, "CREW_03");

        Assert.Empty(h.Launch("CREW_02", "CREW_01").OfType<MoraleChanged>());
        MoraleChanged morale = Assert.Single(h.Launch("CREW_02", "CREW_03").OfType<MoraleChanged>());

        Assert.Equal(("CREW_02", -3), (morale.CrewId, morale.To - morale.From));
        Assert.Equal(2, h.Trait("trait_g", "CREW_02", "CREW_03"));
    }

    [Fact]
    public void WithTarget_bonus_only_when_both_are_on_the_team()
    {
        var h = new Harness(Pack(2));
        SetUp(h, "trait_g", "CREW_02", 2, "CREW_03");
        SetUp(h, "trait_h", "CREW_02", 1, "CREW_04");
        ITraitView view = h.Bus.View<ITraitView>(h.State);

        Assert.Empty(view.Bonuses("CREW_02", ["CREW_02", "CREW_01"]));
        Assert.Equal([new TraitBonus("trait_g", -1)], view.Bonuses("CREW_02", ["CREW_02", "CREW_03"]));
        Assert.Equal([new TraitBonus("trait_g", -1), new TraitBonus("trait_h", 1)], view.Bonuses("CREW_02", ["CREW_03", "CREW_02", "CREW_04"]));
        Assert.Empty(view.Bonuses("CREW_03", ["CREW_02", "CREW_03"]));
    }

    // Focus 4: the rules run from the preview. Every entry the preview lists for this team fires, in its order, and nothing else does.
    [Fact]
    public void The_rules_apply_what_Preview_says()
    {
        var h = new Harness(Pack(2));
        SetUp(h, "trait_e", "CREW_03", 1);
        SetUp(h, "trait_g", "CREW_02", 2, "CREW_03");
        SetUp(h, "trait_e", "CREW_01", 1);
        IReadOnlyList<LaunchPreview> preview = h.Bus.View<ITraitView>(h.State).Preview(h.Bus.View<IRosterView>(h.State).Roster);
        string[] team = ["CREW_02", "CREW_03"];

        Assert.Equal(
            [("trait_e", "CREW_01", "sent"), ("trait_e", "CREW_01", "notSent"), ("trait_g", "CREW_02", "withTarget"), ("trait_e", "CREW_03", "sent"), ("trait_e", "CREW_03", "notSent")],
            preview.Select(p => (p.Trait, p.HolderId, p.When)));
        List<IEvent> events = h.Launch(team);

        Assert.Equal(
            [new TraitChanged("trait_e", "CREW_01", null, 1, 0, TraitCauses.Launch, null), new TraitChanged("trait_e", "CREW_03", null, 1, 2, TraitCauses.Launch, null)],
            Changes(events));
        Assert.Equal(["CREW_02", "CREW_03"], events.OfType<MoraleChanged>().Select(m => m.CrewId));
        Assert.Equal(preview.Where(p => p.Fires(team)).Sum(p => p.Effects.Count(e => e.Morale != 0)), events.OfType<MoraleChanged>().Count());
    }
}

using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Cycle;
using Sim.Traits;
using static Sim.Tests.StoryletsModuleTests;

namespace Sim.Tests;

// The traits module (slice 1e, part three): storage, changes, and Dev Play's sets. These were the storylets module's flag
// tests until Task 1 of part three carved the module out; the harness is the storylets module's, which builds both.
public class TraitsModuleTests
{
    [Fact]
    public void A_missions_trait_is_stored_with_the_week_and_the_encounter()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Advance);
        h.To(CyclePhase.Resolution);

        List<IEvent> events = h.End([Raise("trait_a", 2), Fix("trait_b", 3, "CREW_02", "ENCOUNTER_04")]);

        Assert.Equal(2, h.State.Week);
        Assert.Equal(
            [new TraitChanged("trait_a", null, null, 0, 2, TraitCauses.Mission, Encounter), new TraitChanged("trait_b", "CREW_02", null, 0, 3, TraitCauses.Mission, "ENCOUNTER_04")],
            events.OfType<TraitChanged>());
        Assert.Equal(
            [("trait_a", null, 2, 2, Encounter), ("trait_b", "CREW_02", 3, 2, "ENCOUNTER_04")],
            h.Traits.Traits.Select(t => (t.Trait, t.CrewId, t.Value, t.ChangedAtWeek, t.EncounterId)));

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

        Assert.Contains(new CrewDied("CREW_02", "MISSION_1"), events);
        Assert.Equal([new TraitChanged("trait_b", "CREW_03", null, 0, 1, TraitCauses.Mission, Encounter)], events.OfType<TraitChanged>());
        Assert.Equal(["CREW_03"], h.Traits.Traits.Select(t => t.CrewId));
        Assert.Equal(["CREW_03"], events.OfType<StoryletOffered>().Select(o => o.SubjectId));
    }

    [Fact]
    public void An_answers_and_a_vignettes_changes_carry_their_cause()
    {
        var glance = new VignetteDef("VIGNETTE_GLANCE", StoryletScopes.Ship, [new TraitRange("trait_a", 1, null)], [Add("trait_c", 1, "ship")], [], null);
        var h = new Harness(Pack(2, Quirk) with { Vignettes = [glance] });

        List<IEvent> arrived = h.Mission(Raise("trait_a", 1));
        List<IEvent> answered = h.Send(new AnswerStorylet("2:STORYLET_QUIRK:ship", "ride"));

        Assert.Equal([new TraitChanged("trait_c", null, null, 0, 1, TraitCauses.Vignette, null)], arrived.OfType<TraitChanged>().Where(t => t.Trait == "trait_c"));
        Assert.Equal([new TraitChanged("trait_a", null, null, 1, 2, TraitCauses.Answer, null)], answered.OfType<TraitChanged>());
    }

    [Fact]
    public void A_developers_trait_is_its_own_event_keeps_where_the_trait_came_from_and_brings_its_storylet_at_the_next_itinerary()
    {
        var h = new Harness(Pack(2, Quirk, Shaken));
        h.Mission(Raise("trait_a", 1));
        h.Send(new AnswerStorylet("2:STORYLET_QUIRK:ship", "clear"));
        h.To(CyclePhase.Orders);

        List<IEvent> ship = h.Send(new SetTrait("trait_a", null, null, 1));
        List<IEvent> crew = h.Send(new SetTrait("trait_b", "CREW_03", null, 2));

        Assert.Equal([new TraitSetByDeveloper("trait_a", null, null, 1), new TraitChanged("trait_a", null, null, 0, 1, TraitCauses.Developer, null)], ship);
        Assert.Equal([new TraitSetByDeveloper("trait_b", "CREW_03", null, 2), new TraitChanged("trait_b", "CREW_03", null, 0, 2, TraitCauses.Developer, null)], crew);
        Assert.Empty(h.Waiting);

        List<IEvent> itinerary = h.To(CyclePhase.Itinerary);

        Assert.Equal(["3:STORYLET_SHAKEN:CREW_03", "3:STORYLET_QUIRK:ship"], itinerary.OfType<StoryletOffered>().Select(o => o.OfferId));
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

    // One week: on to the next cycle's itinerary. The events of the move.
    private static List<IEvent> Week(Harness h)
    {
        return h.To(CyclePhase.Itinerary);
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
        int set = h.State.Week;

        Assert.Empty(Changes(Week(h)));
        Assert.Equal([new TraitChanged("trait_x", null, null, 3, 2, TraitCauses.Weekly, null)], Changes(Week(h)));
        Assert.Equal(set + 2, h.Traits.Traits.Single().ChangedAtWeek);
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

    [Fact]
    public void Nothing_is_published_when_nothing_changes()
    {
        Harness h = With(Def("trait_x", max: 2));
        h.Send(new ChangeTrait("trait_x", null, null, 2, true, TraitCauses.Answer));
        int week = h.Traits.Traits.Single().ChangedAtWeek;
        Week(h);

        Assert.Empty(h.Send(new ChangeTrait("trait_x", null, null, 2, true, TraitCauses.Answer)));
        Assert.Empty(h.Send(new ChangeTrait("trait_x", null, null, 1, false, TraitCauses.Answer)));
        Assert.Empty(h.Send(new ChangeTrait("trait_y", null, null, 0, true, TraitCauses.Answer)));
        Assert.Empty(h.Send(new ChangeTrait("trait_y", null, null, -1, false, TraitCauses.Answer)));
        Assert.Equal(week, h.Traits.Traits.Single().ChangedAtWeek);
    }
}

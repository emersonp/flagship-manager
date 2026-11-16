using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;
using Sim.Storylets;

namespace Sim.Tests;

public class StoryletsModuleTests
{
    private const string Encounter = "ENCOUNTER_01";

    private sealed record EndMission(MissionResult Result) : ICommand;

    // Cycle, crew, and storylets on one bus; a test command stands in for the missions module's end of a mission.
    private sealed class Harness
    {
        public Harness(ContentPack pack)
        {
            State = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };
            Bus = Build(pack);
            Send(new FoundLegacy());
        }

        public EventBus Bus { get; private set; }

        public LegacyState State { get; }

        public StoryletsState Storylets => State.Module<StoryletsState>(StoryletsModule.Name);

        public CrewState Crew => State.Module<CrewState>(CrewModule.Name);

        // The same legacy on a bus built from another pack, as when the author edits the content between requests.
        public void Rebuild(ContentPack pack)
        {
            Bus = Build(pack);
        }

        public List<IEvent> Send(ICommand command)
        {
            return Bus.Send(State, command).Select(p => p.Event).ToList();
        }

        public string Refused(ICommand command)
        {
            return Assert.Throws<CommandRejectedException>(() => Bus.Send(State, command)).Message;
        }

        // Advances until the phase is reached, in this cycle or the next.
        public List<IEvent> To(CyclePhase phase)
        {
            var events = new List<IEvent>();
            do
            {
                events.AddRange(Send(new AdvancePhase()));
            }
            while (State.Phase != phase);

            return events;
        }

        public List<IEvent> End(MissionFlag[] flags, params CrewResult[] crew)
        {
            return Send(new EndMission(new MissionResult(MissionOutcome.PartialSuccess, 1, 2, 0, 0, crew, flags)));
        }

        // A mission that changes these flags, then the aftermath it leads to; the events of both.
        public List<IEvent> Mission(params MissionFlag[] flags)
        {
            var events = To(CyclePhase.Resolution);
            events.AddRange(End(flags));
            events.AddRange(To(CyclePhase.Aftermath));
            return events;
        }

        public int Flag(string flag, string? crewId = null)
        {
            return Storylets.Flags.FirstOrDefault(f => f.Flag == flag && f.CrewId == crewId)?.Value ?? 0;
        }

        public List<string> Waiting => Storylets.Waiting.Select(w => w.OfferId).ToList();

        private static EventBus Build(ContentPack pack)
        {
            var bus = new EventBus();
            CycleModule.Register(bus);
            CrewModule.Register(bus, pack);
            StoryletsModule.Register(bus, pack);
            bus.Handle<EndMission>((command, context) => context.Publish(new MissionEnded("MISSION_1", "OBJECTIVE_1", command.Result)));
            return bus;
        }
    }

    private static ContentPack Pack(int perCycle, params StoryletDef[] storylets)
    {
        return TestContent.Pack with { Storylets = storylets, StoryletsPerCycle = perCycle };
    }

    private static StoryletDef Ship(string id, int stakes, FlagRange[] requires, params StoryletOption[] options)
    {
        return new StoryletDef(id, stakes, StoryletScopes.Ship, requires, options);
    }

    private static StoryletDef Crew(string id, int stakes, FlagRange[] requires, params StoryletOption[] options)
    {
        return new StoryletDef(id, stakes, StoryletScopes.Crew, requires, options);
    }

    private static StoryletOption Option(string id, params StoryletEffect[] effects)
    {
        return new StoryletOption(id, id.ToUpperInvariant(), effects);
    }

    private static StoryletEffect Set(string flag, int value, string on = "")
    {
        return new StoryletEffect(Flag: new FlagChange(flag, value, true, on));
    }

    private static StoryletEffect Add(string flag, int amount, string on = "")
    {
        return new StoryletEffect(Flag: new FlagChange(flag, amount, false, on));
    }

    private static StoryletEffect Loyalty(int amount, string on)
    {
        return new StoryletEffect(Loyalty: amount, LoyaltyOn: on);
    }

    private static MissionFlag Raise(string flag, int amount, string? crewId = null, string encounter = Encounter)
    {
        return new MissionFlag(flag, amount, false, crewId, encounter);
    }

    private static MissionFlag Fix(string flag, int value, string? crewId = null, string encounter = Encounter)
    {
        return new MissionFlag(flag, value, true, crewId, encounter);
    }

    private static CrewResult Died(string id)
    {
        return new CrewResult(id, MemberStatus.Dead, 0, new Dictionary<string, double>(), 0);
    }

    // The ship storylet of the fixture: flag_a at 1, cleared or moved on to 2.
    private static readonly StoryletDef Quirk = Ship("STORYLET_QUIRK", 2, [new FlagRange("flag_a", 1, 1)], Option("clear", Set("flag_a", 0)), Option("ride", Set("flag_a", 2)));

    // A crew storylet: flag_b at 1 or more on someone.
    private static readonly StoryletDef Shaken = Crew("STORYLET_SHAKEN", 3, [new FlagRange("flag_b", 1, null)], Option("calm", Add("flag_b", -1)), Option("ignore", Set("flag_b", 0)));

    [Fact]
    public void A_missions_flag_is_stored_with_the_week_and_the_encounter()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Advance);
        h.To(CyclePhase.Resolution);

        List<IEvent> events = h.End([Raise("flag_a", 2), Fix("flag_b", 3, "CREW_02", "ENCOUNTER_04")]);

        Assert.Equal(2, h.State.Week);
        Assert.Equal(
            [new FlagChanged("flag_a", null, 0, 2, Encounter), new FlagChanged("flag_b", "CREW_02", 0, 3, "ENCOUNTER_04")],
            events.OfType<FlagChanged>());
        Assert.Equal(
            [("flag_a", null, 2, 2, Encounter), ("flag_b", "CREW_02", 3, 2, "ENCOUNTER_04")],
            h.Storylets.Flags.Select(f => (f.Flag, f.CrewId, f.Value, f.SetAtWeek, f.EncounterId)));

        // The state is stored as JSON with the legacy, so it must read back whole.
        string json = JsonSerializer.Serialize(h.Storylets, SimJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<StoryletsState>(json, SimJson.Options), SimJson.Options));
    }

    [Fact]
    public void Add_and_set_never_go_below_zero_and_a_flag_at_zero_is_gone()
    {
        var h = new Harness(Pack(2));
        h.To(CyclePhase.Resolution);

        h.End([Raise("flag_a", 2), Fix("flag_b", 3)]);
        List<IEvent> events = h.End([Raise("flag_a", -5), Fix("flag_b", 0), Raise("flag_c", -1), Fix("flag_d", 0)]);

        Assert.Equal([new FlagChanged("flag_a", null, 2, 0, Encounter), new FlagChanged("flag_b", null, 3, 0, Encounter)], events.OfType<FlagChanged>());
        Assert.Empty(h.Storylets.Flags);

        h.End([Raise("flag_a", 1), Raise("flag_a", 1), Fix("flag_a", 1)]);

        Assert.Equal(1, h.Flag("flag_a"));
        Assert.Single(h.Storylets.Flags);
    }

    [Fact]
    public void A_flag_on_someone_who_died_on_that_mission_is_not_stored()
    {
        var h = new Harness(Pack(2, Shaken));
        h.To(CyclePhase.Resolution);

        List<IEvent> events = h.End([Raise("flag_b", 1, "CREW_02"), Raise("flag_b", 1, "CREW_03")], Died("CREW_02"));
        events.AddRange(h.To(CyclePhase.Aftermath));

        Assert.Contains(new CrewDied("CREW_02", "MISSION_1"), events);
        Assert.Equal([new FlagChanged("flag_b", "CREW_03", 0, 1, Encounter)], events.OfType<FlagChanged>());
        Assert.Equal(["CREW_03"], h.Storylets.Flags.Select(f => f.CrewId));
        Assert.Equal(["CREW_03"], events.OfType<StoryletOffered>().Select(o => o.SubjectId));
    }

    [Fact]
    public void A_storylet_whose_requirements_hold_is_offered_when_the_aftermath_begins()
    {
        var h = new Harness(Pack(2, Quirk));
        h.To(CyclePhase.Resolution);

        List<IEvent> ended = h.End([Raise("flag_a", 1)]);
        List<IEvent> aftermath = h.To(CyclePhase.Aftermath);

        Assert.DoesNotContain(ended, e => e is StoryletOffered);
        Assert.Equal([new StoryletOffered("1:STORYLET_QUIRK:ship", "STORYLET_QUIRK", null, Encounter)], aftermath.OfType<StoryletOffered>());
        WaitingStorylet waiting = Assert.Single(h.Storylets.Waiting);
        Assert.Equal(("1:STORYLET_QUIRK:ship", "STORYLET_QUIRK", (string?)null, (string?)Encounter), (waiting.OfferId, waiting.StoryletId, waiting.SubjectId, waiting.EncounterId));
    }

    [Fact]
    public void A_storylet_for_the_ship_ignores_the_same_flag_on_a_crew_member_and_the_reverse()
    {
        var h = new Harness(Pack(4, Quirk, Shaken));

        List<IEvent> events = h.Mission(Raise("flag_a", 1, "CREW_01"), Raise("flag_b", 1));

        Assert.DoesNotContain(events, e => e is StoryletOffered);
        Assert.Empty(h.Waiting);
    }

    [Fact]
    public void At_most_the_cycles_limit_are_offered_highest_stakes_first_and_the_rest_next_cycle()
    {
        var h = new Harness(Pack(2, Quirk, Shaken));

        // The flags are reported in another order than the roster's: the order of the offers is the roster's.
        List<IEvent> first = h.Mission(Raise("flag_a", 1), Raise("flag_b", 1, "CREW_03"), Raise("flag_b", 1, "CREW_01"));

        Assert.Equal(["1:STORYLET_SHAKEN:CREW_01", "1:STORYLET_SHAKEN:CREW_03"], first.OfType<StoryletOffered>().Select(o => o.OfferId));
        Assert.Equal(["1:STORYLET_SHAKEN:CREW_01", "1:STORYLET_SHAKEN:CREW_03"], h.Waiting);

        h.Send(new AnswerStorylet("1:STORYLET_SHAKEN:CREW_01", "calm"));
        h.Send(new AnswerStorylet("1:STORYLET_SHAKEN:CREW_03", "calm"));
        List<IEvent> next = h.To(CyclePhase.Aftermath);

        Assert.Equal(2, h.State.Cycle);
        Assert.Equal(["2:STORYLET_QUIRK:ship"], next.OfType<StoryletOffered>().Select(o => o.OfferId));
    }

    [Fact]
    public void An_answer_applies_its_effects_in_order_and_the_storylet_does_not_return()
    {
        StoryletDef storylet = Ship("STORYLET_ORDER", 1, [new FlagRange("flag_a", 1, 1)], Option("first", Set("flag_a", 5), Add("flag_a", -5), Add("flag_b", 1, "ship")), Option("second", Set("flag_a", 0)));
        var h = new Harness(Pack(2, storylet));
        h.Mission(Raise("flag_a", 1));

        List<IEvent> events = h.Send(new AnswerStorylet("1:STORYLET_ORDER:ship", "first"));

        Assert.Equal(
            [
                new FlagChanged("flag_a", null, 1, 5, null),
                new FlagChanged("flag_a", null, 5, 0, null),
                new FlagChanged("flag_b", null, 0, 1, null),
                new StoryletAnswered("1:STORYLET_ORDER:ship", "STORYLET_ORDER", null, "first"),
            ],
            events);
        Assert.Empty(h.Waiting);
        Assert.Equal((0, 1), (h.Flag("flag_a"), h.Flag("flag_b")));
        StoredFlag stored = h.Storylets.Flags.Single();
        Assert.Equal((1, (string?)null), (stored.SetAtWeek, stored.EncounterId));

        List<IEvent> later = h.To(CyclePhase.Aftermath);
        later.AddRange(h.To(CyclePhase.Aftermath));

        Assert.DoesNotContain(later, e => e is StoryletOffered);
    }

    [Fact]
    public void An_answer_that_moves_the_flag_on_lets_another_storylet_require_the_new_value()
    {
        StoryletDef known = Ship("STORYLET_KNOWN", 1, [new FlagRange("flag_a", 2, 2)], Option("fix", Set("flag_a", 0)), Option("drop", Set("flag_a", 3)));
        var h = new Harness(Pack(2, Quirk, known));
        h.Mission(Raise("flag_a", 1));

        h.Send(new AnswerStorylet("1:STORYLET_QUIRK:ship", "ride"));
        List<IEvent> next = h.To(CyclePhase.Aftermath);

        Assert.Equal(2, h.Flag("flag_a"));
        StoredFlag stored = h.Storylets.Flags.Single();
        Assert.Equal(Encounter, stored.EncounterId);
        Assert.Equal([new StoryletOffered("2:STORYLET_KNOWN:ship", "STORYLET_KNOWN", null, Encounter)], next.OfType<StoryletOffered>());
    }

    [Fact]
    public void Loyalty_on_crew_reaches_every_living_member_and_is_clamped()
    {
        StoryletDef storylet = Crew("STORYLET_LOYAL", 1, [new FlagRange("flag_b", 1, null)], Option("everyone", Loyalty(-3, "crew"), Set("flag_b", 0), Loyalty(2, "subject")), Option("other", Set("flag_b", 0)));
        var h = new Harness(Pack(2, storylet));
        h.To(CyclePhase.Resolution);
        h.End([Raise("flag_b", 1, "CREW_02")], Died("CREW_08"));
        h.To(CyclePhase.Aftermath);
        h.Crew.Members.Single(m => m.Id == "CREW_01").Loyalty = 1;
        h.Crew.Members.Single(m => m.Id == "CREW_02").Loyalty = 99;

        List<IEvent> events = h.Send(new AnswerStorylet("1:STORYLET_LOYAL:CREW_02", "everyone"));

        List<LoyaltyChanged> changes = events.OfType<LoyaltyChanged>().ToList();
        Assert.Equal(
            [
                new LoyaltyChanged("CREW_01", 1, 0),
                new LoyaltyChanged("CREW_02", 99, 96),
                new LoyaltyChanged("CREW_03", 50, 47),
                new LoyaltyChanged("CREW_04", 50, 47),
                new LoyaltyChanged("CREW_05", 50, 47),
                new LoyaltyChanged("CREW_06", 50, 47),
                new LoyaltyChanged("CREW_07", 50, 47),
                new LoyaltyChanged("CREW_02", 96, 98),
            ],
            changes);
        Assert.Equal(0, h.Crew.Members.Single(m => m.Id == "CREW_01").Loyalty);

        // At the bound nothing changes, so nothing is published.
        h.Crew.Members.Single(m => m.Id == "CREW_01").Loyalty = 0;
        Assert.Empty(h.Send(new AdjustLoyalty("CREW_01", -1)));
        Assert.Equal([new LoyaltyChanged("CREW_02", 98, 100)], h.Send(new AdjustLoyalty("CREW_02", 7)));
        Assert.Equal("Crew member 'CREW_08' is not on the roster.", h.Refused(new AdjustLoyalty("CREW_08", 1)));
    }

    [Fact]
    public void A_second_answer_an_unknown_offer_and_an_unknown_option_are_refused()
    {
        var h = new Harness(Pack(2, Quirk));
        h.Mission(Raise("flag_a", 1));
        string before = JsonSerializer.Serialize(h.Storylets, SimJson.Options);

        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("1:STORYLET_OTHER:ship", "clear")));
        Assert.Equal("That is not one of its answers.", h.Refused(new AnswerStorylet("1:STORYLET_QUIRK:ship", "option_z")));
        Assert.Equal(before, JsonSerializer.Serialize(h.Storylets, SimJson.Options));

        h.Send(new AnswerStorylet("1:STORYLET_QUIRK:ship", "clear"));
        string after = JsonSerializer.Serialize(h.Storylets, SimJson.Options);

        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("1:STORYLET_QUIRK:ship", "clear")));
        Assert.Equal(after, JsonSerializer.Serialize(h.Storylets, SimJson.Options));
    }

    [Fact]
    public void The_aftermath_cannot_be_left_while_a_storylet_waits()
    {
        var h = new Harness(Pack(2, Quirk));
        h.Mission(Raise("flag_a", 1));

        Assert.Equal("A storylet is waiting for an answer.", h.Refused(new AdvancePhase()));
        Assert.Equal(CyclePhase.Aftermath, h.State.Phase);

        h.Send(new AnswerStorylet("1:STORYLET_QUIRK:ship", "clear"));
        h.Send(new AdvancePhase());

        Assert.Equal(CyclePhase.Advance, h.State.Phase);
    }

    [Fact]
    public void A_storylet_still_waits_in_a_cycle_where_no_team_was_sent()
    {
        StoryletDef second = Ship("STORYLET_SECOND", 1, [new FlagRange("flag_b", 1, null)], Option("clear", Set("flag_b", 0)), Option("also", Set("flag_b", 0)));
        var h = new Harness(Pack(1, Quirk, second));
        h.Mission(Raise("flag_a", 1), Raise("flag_b", 1));
        Assert.Equal(["1:STORYLET_QUIRK:ship"], h.Waiting);
        h.Send(new AnswerStorylet("1:STORYLET_QUIRK:ship", "clear"));

        // Cycle 2: nobody is sent, and no mission ends.
        List<IEvent> events = h.To(CyclePhase.Aftermath);

        Assert.DoesNotContain(events, e => e is MissionEnded);
        Assert.Equal(["2:STORYLET_SECOND:ship"], h.Waiting);
        Assert.Equal("A storylet is waiting for an answer.", h.Refused(new AdvancePhase()));
        h.Send(new AnswerStorylet("2:STORYLET_SECOND:ship", "clear"));
        h.Send(new AdvancePhase());
        Assert.Equal(CyclePhase.Advance, h.State.Phase);
    }

    [Fact]
    public void A_waiting_storylet_the_pack_no_longer_has_is_withdrawn()
    {
        var h = new Harness(Pack(2, Quirk));
        h.Mission(Raise("flag_a", 1));
        h.Rebuild(Pack(2));

        // It can no longer be answered, and it does not hold the legacy in the aftermath.
        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("1:STORYLET_QUIRK:ship", "clear")));
        h.Send(new AdvancePhase());
        List<IEvent> next = h.To(CyclePhase.Aftermath);

        Assert.Equal([new StoryletWithdrawn("1:STORYLET_QUIRK:ship", "STORYLET_QUIRK", null)], next.OfType<StoryletWithdrawn>());
        Assert.Empty(h.Waiting);
        Assert.Equal(1, h.Flag("flag_a"));
    }

    [Fact]
    public void A_dead_subjects_flags_and_waiting_storylets_are_dropped()
    {
        var h = new Harness(Pack(2, Shaken));
        h.Mission(Raise("flag_b", 1, "CREW_02"), Raise("flag_c", 2, "CREW_02"), Raise("flag_b", 1, "CREW_03"), Raise("flag_a", 1));
        Assert.Equal(["1:STORYLET_SHAKEN:CREW_02", "1:STORYLET_SHAKEN:CREW_03"], h.Waiting);

        List<IEvent> events = h.End([], Died("CREW_02"));

        Assert.Equal([new StoryletWithdrawn("1:STORYLET_SHAKEN:CREW_02", "STORYLET_SHAKEN", "CREW_02")], events.OfType<StoryletWithdrawn>());
        Assert.Equal(["1:STORYLET_SHAKEN:CREW_03"], h.Waiting);
        Assert.Equal([("flag_b", "CREW_03"), ("flag_a", (string?)null)], h.Storylets.Flags.Select(f => (f.Flag, f.CrewId)));
    }
}

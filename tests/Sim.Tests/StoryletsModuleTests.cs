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

        // A mission that changes these flags, then the next cycle's itinerary, where its storylets arrive; the events of both.
        public List<IEvent> Mission(params MissionFlag[] flags)
        {
            var events = To(CyclePhase.Resolution);
            events.AddRange(End(flags));
            events.AddRange(To(CyclePhase.Itinerary));
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
        // No vignettes: their arrivals and effects belong to their own tests.
        return TestContent.Pack with { Storylets = storylets, StoryletsPerCycle = perCycle, Vignettes = [] };
    }

    private static StoryletDef Ship(string id, int stakes, Requirement[] requires, params StoryletOption[] options)
    {
        return new StoryletDef(id, stakes, StoryletScopes.Ship, requires, options);
    }

    private static StoryletDef Crew(string id, int stakes, Requirement[] requires, params StoryletOption[] options)
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
        events.AddRange(h.To(CyclePhase.Itinerary));

        Assert.Contains(new CrewDied("CREW_02", "MISSION_1"), events);
        Assert.Equal([new FlagChanged("flag_b", "CREW_03", 0, 1, Encounter)], events.OfType<FlagChanged>());
        Assert.Equal(["CREW_03"], h.Storylets.Flags.Select(f => f.CrewId));
        Assert.Equal(["CREW_03"], events.OfType<StoryletOffered>().Select(o => o.SubjectId));
    }

    [Fact]
    public void A_storylet_whose_requirements_hold_is_offered_when_the_itinerary_opens()
    {
        var h = new Harness(Pack(2, Quirk));
        h.To(CyclePhase.Resolution);

        List<IEvent> ended = h.End([Raise("flag_a", 1)]);
        List<IEvent> itinerary = h.To(CyclePhase.Itinerary);

        Assert.DoesNotContain(ended, e => e is StoryletOffered);
        Assert.Equal([new StoryletOffered("2:STORYLET_QUIRK:ship", "STORYLET_QUIRK", null, Encounter)], itinerary.OfType<StoryletOffered>());
        WaitingStorylet waiting = Assert.Single(h.Storylets.Waiting);
        Assert.Equal(("2:STORYLET_QUIRK:ship", "STORYLET_QUIRK", (string?)null, (string?)Encounter), (waiting.OfferId, waiting.StoryletId, waiting.SubjectId, waiting.EncounterId));
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

        Assert.Equal(["2:STORYLET_SHAKEN:CREW_01", "2:STORYLET_SHAKEN:CREW_03"], first.OfType<StoryletOffered>().Select(o => o.OfferId));
        Assert.Equal(["2:STORYLET_SHAKEN:CREW_01", "2:STORYLET_SHAKEN:CREW_03"], h.Waiting);

        h.Send(new AnswerStorylet("2:STORYLET_SHAKEN:CREW_01", "calm"));
        h.Send(new AnswerStorylet("2:STORYLET_SHAKEN:CREW_03", "calm"));
        List<IEvent> next = h.To(CyclePhase.Itinerary);

        Assert.Equal(3, h.State.Cycle);
        Assert.Equal(["3:STORYLET_QUIRK:ship"], next.OfType<StoryletOffered>().Select(o => o.OfferId));
    }

    [Fact]
    public void An_answer_applies_its_effects_in_order_and_the_storylet_does_not_return()
    {
        StoryletDef storylet = Ship("STORYLET_ORDER", 1, [new FlagRange("flag_a", 1, 1)], Option("first", Set("flag_a", 5), Add("flag_a", -5), Add("flag_b", 1, "ship")), Option("second", Set("flag_a", 0)));
        var h = new Harness(Pack(2, storylet));
        h.Mission(Raise("flag_a", 1));

        List<IEvent> events = h.Send(new AnswerStorylet("2:STORYLET_ORDER:ship", "first"));

        Assert.Equal(
            [
                new FlagChanged("flag_a", null, 1, 5, null),
                new FlagChanged("flag_a", null, 5, 0, null),
                new FlagChanged("flag_b", null, 0, 1, null),
                new StoryletAnswered("2:STORYLET_ORDER:ship", "STORYLET_ORDER", null, "first"),
            ],
            events);
        Assert.Empty(h.Waiting);
        Assert.Equal((0, 1), (h.Flag("flag_a"), h.Flag("flag_b")));
        StoredFlag stored = h.Storylets.Flags.Single();
        Assert.Equal((2, (string?)null), (stored.SetAtWeek, stored.EncounterId));

        List<IEvent> later = h.To(CyclePhase.Itinerary);
        later.AddRange(h.To(CyclePhase.Itinerary));

        Assert.DoesNotContain(later, e => e is StoryletOffered);
    }

    [Fact]
    public void An_answer_that_moves_the_flag_on_lets_another_storylet_require_the_new_value()
    {
        StoryletDef known = Ship("STORYLET_KNOWN", 1, [new FlagRange("flag_a", 2, 2)], Option("fix", Set("flag_a", 0)), Option("drop", Set("flag_a", 3)));
        var h = new Harness(Pack(2, Quirk, known));
        h.Mission(Raise("flag_a", 1));

        h.Send(new AnswerStorylet("2:STORYLET_QUIRK:ship", "ride"));
        List<IEvent> next = h.To(CyclePhase.Itinerary);

        Assert.Equal(2, h.Flag("flag_a"));
        StoredFlag stored = h.Storylets.Flags.Single();
        Assert.Equal(Encounter, stored.EncounterId);
        Assert.Equal([new StoryletOffered("3:STORYLET_KNOWN:ship", "STORYLET_KNOWN", null, Encounter)], next.OfType<StoryletOffered>());
    }

    [Fact]
    public void Loyalty_on_crew_reaches_every_living_member_and_is_clamped()
    {
        StoryletDef storylet = Crew("STORYLET_LOYAL", 1, [new FlagRange("flag_b", 1, null)], Option("everyone", Loyalty(-3, "crew"), Set("flag_b", 0), Loyalty(2, "subject")), Option("other", Set("flag_b", 0)));
        var h = new Harness(Pack(2, storylet));
        h.To(CyclePhase.Resolution);
        h.End([Raise("flag_b", 1, "CREW_02")], Died("CREW_08"));
        h.To(CyclePhase.Itinerary);
        h.Crew.Members.Single(m => m.Id == "CREW_01").Loyalty = 1;
        h.Crew.Members.Single(m => m.Id == "CREW_02").Loyalty = 99;

        List<IEvent> events = h.Send(new AnswerStorylet("2:STORYLET_LOYAL:CREW_02", "everyone"));

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

        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("2:STORYLET_OTHER:ship", "clear")));
        Assert.Equal("That is not one of its answers.", h.Refused(new AnswerStorylet("2:STORYLET_QUIRK:ship", "option_z")));
        Assert.Equal(before, JsonSerializer.Serialize(h.Storylets, SimJson.Options));

        h.Send(new AnswerStorylet("2:STORYLET_QUIRK:ship", "clear"));
        string after = JsonSerializer.Serialize(h.Storylets, SimJson.Options);

        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("2:STORYLET_QUIRK:ship", "clear")));
        Assert.Equal(after, JsonSerializer.Serialize(h.Storylets, SimJson.Options));
    }

    [Fact]
    public void The_itinerary_cannot_be_left_while_a_storylet_waits()
    {
        var h = new Harness(Pack(2, Quirk));
        h.Mission(Raise("flag_a", 1));

        Assert.Equal("A storylet is waiting for an answer.", h.Refused(new AdvancePhase()));
        Assert.Equal(CyclePhase.Itinerary, h.State.Phase);

        h.Send(new AnswerStorylet("2:STORYLET_QUIRK:ship", "clear"));
        h.Send(new AdvancePhase());

        Assert.Equal(CyclePhase.Orders, h.State.Phase);
    }

    [Fact]
    public void A_storylet_still_waits_in_a_cycle_where_no_team_was_sent()
    {
        StoryletDef second = Ship("STORYLET_SECOND", 1, [new FlagRange("flag_b", 1, null)], Option("clear", Set("flag_b", 0)), Option("also", Set("flag_b", 0)));
        var h = new Harness(Pack(1, Quirk, second));
        h.Mission(Raise("flag_a", 1), Raise("flag_b", 1));
        Assert.Equal(["2:STORYLET_QUIRK:ship"], h.Waiting);
        h.Send(new AnswerStorylet("2:STORYLET_QUIRK:ship", "clear"));

        // Cycle 2: nobody is sent, and no mission ends.
        List<IEvent> events = h.To(CyclePhase.Itinerary);

        Assert.DoesNotContain(events, e => e is MissionEnded);
        Assert.Equal(["3:STORYLET_SECOND:ship"], h.Waiting);
        Assert.Equal("A storylet is waiting for an answer.", h.Refused(new AdvancePhase()));
        h.Send(new AnswerStorylet("3:STORYLET_SECOND:ship", "clear"));
        h.Send(new AdvancePhase());
        Assert.Equal(CyclePhase.Orders, h.State.Phase);
    }

    [Fact]
    public void A_waiting_storylet_the_pack_no_longer_has_is_withdrawn()
    {
        var h = new Harness(Pack(2, Quirk));
        h.Mission(Raise("flag_a", 1));
        h.Rebuild(Pack(2));

        // It can no longer be answered, and it does not hold the legacy in the itinerary.
        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("2:STORYLET_QUIRK:ship", "clear")));
        h.Send(new AdvancePhase());
        List<IEvent> next = h.To(CyclePhase.Itinerary);

        Assert.Equal([new StoryletWithdrawn("2:STORYLET_QUIRK:ship", "STORYLET_QUIRK", null)], next.OfType<StoryletWithdrawn>());
        Assert.Empty(h.Waiting);
        Assert.Equal(1, h.Flag("flag_a"));
    }

    [Fact]
    public void A_dead_subjects_flags_and_waiting_storylets_are_dropped()
    {
        var h = new Harness(Pack(2, Shaken));
        h.Mission(Raise("flag_b", 1, "CREW_02"), Raise("flag_c", 2, "CREW_02"), Raise("flag_b", 1, "CREW_03"), Raise("flag_a", 1));
        Assert.Equal(["2:STORYLET_SHAKEN:CREW_02", "2:STORYLET_SHAKEN:CREW_03"], h.Waiting);

        List<IEvent> events = h.End([], Died("CREW_02"));

        Assert.Equal([new StoryletWithdrawn("2:STORYLET_SHAKEN:CREW_02", "STORYLET_SHAKEN", "CREW_02")], events.OfType<StoryletWithdrawn>());
        Assert.Equal(["2:STORYLET_SHAKEN:CREW_03"], h.Waiting);
        Assert.Equal([("flag_b", "CREW_03"), ("flag_a", (string?)null)], h.Storylets.Flags.Select(f => (f.Flag, f.CrewId)));
    }
    [Fact]
    public void A_developers_flag_is_its_own_event_keeps_where_the_flag_came_from_and_brings_its_storylet_at_the_next_itinerary()
    {
        var h = new Harness(Pack(2, Quirk, Shaken));
        h.Mission(Raise("flag_a", 1));
        h.Send(new AnswerStorylet("2:STORYLET_QUIRK:ship", "clear"));
        h.To(CyclePhase.Orders);

        List<IEvent> ship = h.Send(new SetFlag("flag_a", null, 1));
        List<IEvent> crew = h.Send(new SetFlag("flag_b", "CREW_03", 2));

        Assert.Equal([new FlagSetByDeveloper("flag_a", null, 1), new FlagChanged("flag_a", null, 0, 1, null)], ship);
        Assert.Equal([new FlagSetByDeveloper("flag_b", "CREW_03", 2), new FlagChanged("flag_b", "CREW_03", 0, 2, null)], crew);
        Assert.Empty(h.Waiting);

        List<IEvent> itinerary = h.To(CyclePhase.Itinerary);

        Assert.Equal(["3:STORYLET_SHAKEN:CREW_03", "3:STORYLET_QUIRK:ship"], itinerary.OfType<StoryletOffered>().Select(o => o.OfferId));
        h.Send(new SetFlag("flag_b", "CREW_03", 0));
        Assert.Equal(0, h.Flag("flag_b", "CREW_03"));
    }

    [Theory]
    [InlineData("Flag_A", null, 1, "Flag id 'Flag_A' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("", null, 1, "Flag id '' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("flag_a", null, -1, "A flag's value is 0 to 99.")]
    [InlineData("flag_a", null, 100, "A flag's value is 0 to 99.")]
    [InlineData("flag_a", "CREW_99", 1, "Crew member 'CREW_99' is not on the roster.")]
    [InlineData("flag_a", null, 0, "flag_a is already 0 on the ship.")]
    [InlineData("flag_b", "CREW_02", 0, "flag_b is already 0 on CREW_02.")]
    public void A_developers_flag_is_refused_with_the_reason(string flag, string? crewId, int value, string reason)
    {
        var h = new Harness(Pack(2));

        Assert.Equal(reason, h.Refused(new SetFlag(flag, crewId, value)));
        Assert.Empty(h.Storylets.Flags);
    }

    // 1e part two (design 4.2): requirements on the subject's stats and on the last cycle, and morale as an answer's effect.
    private static readonly FlagRange NoFlagB = new("flag_b", 0, 0);

    private static StoryletDef Fact(string fact, bool not = false)
    {
        return Crew($"STORYLET_{fact.ToUpperInvariant()}{(not ? "_NOT" : "")}", 1, [NoFlagB, new LastCycle(fact, not)], Option("done", Set("flag_b", 1)), Option("other", Set("flag_b", 2)));
    }

    private Dictionary<string, List<string>> WaitingBySubject(Harness h)
    {
        return h.Storylets.Waiting.GroupBy(w => w.StoryletId).ToDictionary(g => g.Key, g => g.Select(w => w.SubjectId!).ToList());
    }

    private static CrewResult Home(string id, int injury = 0)
    {
        return new CrewResult(id, MemberStatus.Active, injury, new Dictionary<string, double>(), 0);
    }

    [Fact]
    public void A_stat_requirement_reads_the_subjects_morale()
    {
        StoryletDef low = Crew("STORYLET_LOW", 1, [new FlagRange("flag_b", 1, null), new StatRange("morale", 0, 25)], Option("calm", Set("flag_b", 0)), Option("ignore", Set("flag_b", 0)));
        var h = new Harness(Pack(8, low));
        h.Crew.Members.Single(m => m.Id == "CREW_02").Morale = 20;

        h.Mission(Raise("flag_b", 1, "CREW_01"), Raise("flag_b", 1, "CREW_02"));

        Assert.Equal(["2:STORYLET_LOW:CREW_02"], h.Waiting);
    }

    // Review focus 1: the dead hold no fact; the living each lost a crewmate.
    [Fact]
    public void Each_last_cycle_fact_holds_for_the_right_crew()
    {
        string[] facts = [LastCycleFacts.Sent, LastCycleFacts.Hurt, LastCycleFacts.Rested, LastCycleFacts.LostCrewmate];
        var h = new Harness(Pack(100, [.. facts.Select(f => Fact(f)), Fact(LastCycleFacts.Sent, not: true)]));
        h.To(CyclePhase.Resolution);
        h.End([], Home("CREW_01"), Home("CREW_02", injury: 1), Died("CREW_03"));

        h.To(CyclePhase.Itinerary);

        Dictionary<string, List<string>> waiting = WaitingBySubject(h);
        Assert.Equal(["CREW_01", "CREW_02"], waiting["STORYLET_SENT"]);
        Assert.Equal(["CREW_02"], waiting["STORYLET_HURT"]);
        Assert.Equal(["CREW_04", "CREW_05", "CREW_06", "CREW_07", "CREW_08"], waiting["STORYLET_RESTED"]);
        Assert.Equal(["CREW_01", "CREW_02", "CREW_04", "CREW_05", "CREW_06", "CREW_07", "CREW_08"], waiting["STORYLET_LOST_CREWMATE"]);
        Assert.Equal(["CREW_04", "CREW_05", "CREW_06", "CREW_07", "CREW_08"], waiting["STORYLET_SENT_NOT"]);
    }

    [Fact]
    public void In_a_cycle_with_no_team_everyone_fit_rested_and_nobody_was_sent()
    {
        var h = new Harness(Pack(100, Fact(LastCycleFacts.Rested), Fact(LastCycleFacts.Sent)));

        h.To(CyclePhase.Itinerary);

        Assert.Equal(8, WaitingBySubject(h)["STORYLET_RESTED"].Count);
        Assert.False(WaitingBySubject(h).ContainsKey("STORYLET_SENT"));
    }

    [Fact]
    public void The_last_cycle_is_forgotten_when_the_next_orders_open()
    {
        var h = new Harness(Pack(100));
        h.To(CyclePhase.Itinerary);
        Assert.NotNull(h.Storylets.LastCycle);

        h.To(CyclePhase.Orders);

        Assert.Null(h.Storylets.LastCycle);
    }

    [Fact]
    public void A_morale_effect_reaches_the_crew_module()
    {
        StoryletDef cheer = Crew("STORYLET_CHEER", 1, [new FlagRange("flag_b", 1, null)],
            Option("cheer", Set("flag_b", 0), new StoryletEffect(Morale: 3, MoraleOn: "subject"), new StoryletEffect(Morale: -1, MoraleOn: "crew")), Option("other", Set("flag_b", 0)));
        var h = new Harness(Pack(2, cheer));
        h.Mission(Raise("flag_b", 1, "CREW_02"));

        List<IEvent> events = h.Send(new AnswerStorylet(h.Waiting.Single(), "cheer"));

        Assert.Contains(new MoraleChanged("CREW_02", 50, 53, MoraleCauses.Answer), events);
        Assert.Equal(52, h.Crew.Members.Single(m => m.Id == "CREW_02").Morale);
        Assert.Equal(49, h.Crew.Members.Single(m => m.Id == "CREW_05").Morale);
    }

    // 1e part two (design 4.3): vignettes arrive when the itinerary opens and leave when it closes.
    private static ContentPack Vignettes(int perCycle, int cooldown, params VignetteDef[] vignettes)
    {
        return TestContent.Pack with { Storylets = [], Vignettes = vignettes, VignettesPerCycle = perCycle, VignetteCooldown = cooldown };
    }

    private static VignetteDef Rested(string id, params StoryletOption[] options)
    {
        return options.Length == 0
            ? new VignetteDef(id, StoryletScopes.Crew, [new LastCycle(LastCycleFacts.Rested, false)], [new StoryletEffect(Morale: 1, MoraleOn: "subject")], [], null)
            : new VignetteDef(id, StoryletScopes.Crew, [new LastCycle(LastCycleFacts.Rested, false)], [], options, options[^1].Id);
    }

    private static List<string> Arrived(IEnumerable<IEvent> events)
    {
        return events.OfType<VignetteArrived>().Select(a => $"{a.VignetteId}:{a.SubjectId ?? "ship"}").ToList();
    }

    [Fact]
    public void A_vignette_with_effects_applies_them_when_it_arrives_and_waits_for_nothing()
    {
        var glance = new VignetteDef("VIGNETTE_GLANCE", StoryletScopes.Ship, [new FlagRange("flag_a", 1, null)], [new StoryletEffect(Morale: 2, MoraleOn: "crew")], [], null);
        var h = new Harness(Vignettes(3, 6, glance));

        List<IEvent> events = h.Mission(Raise("flag_a", 1));

        int at = events.FindIndex(e => e is VignetteArrived);
        Assert.Equal(new VignetteArrived("2:VIGNETTE_GLANCE:ship", "VIGNETTE_GLANCE", null, null), events[at]);
        Assert.Equal(8, events.Skip(at + 1).TakeWhile(e => e is MoraleChanged).Count());
        Assert.Equal(new VignetteAnswered("2:VIGNETTE_GLANCE:ship", "VIGNETTE_GLANCE", null, null, false), events[at + 9]);
        Assert.Empty(h.Storylets.Vignettes);
        Assert.Equal(52, h.Crew.Members[0].Morale);
    }

    // Review focus 3: the cooldown is for one scope; with one a cycle, the next crew member on the roster has it meanwhile.
    [Fact]
    public void A_vignette_in_its_cooldown_does_not_arrive_for_that_scope_only()
    {
        var h = new Harness(Vignettes(1, 6, Rested("VIGNETTE_REST")));
        var arrived = new List<string>();

        for (int cycle = 2; cycle <= 8; cycle++)
        {
            arrived.AddRange(Arrived(h.To(CyclePhase.Itinerary)));
        }

        Assert.Equal(["CREW_01", "CREW_02", "CREW_03", "CREW_04", "CREW_05", "CREW_06", "CREW_01"], arrived.Select(a => a.Split(':')[1]));
    }

    [Fact]
    public void At_most_the_cycles_limit_of_vignettes_arrive_and_the_one_away_longest_comes_first()
    {
        var h = new Harness(Vignettes(3, 0, Rested("VIGNETTE_A"), Rested("VIGNETTE_B")));

        List<string> second = Arrived(h.To(CyclePhase.Itinerary));
        List<string> third = Arrived(h.To(CyclePhase.Itinerary));
        List<string> fourth = Arrived(h.To(CyclePhase.Itinerary));

        Assert.Equal(["VIGNETTE_A:CREW_01", "VIGNETTE_A:CREW_02", "VIGNETTE_A:CREW_03"], second);
        Assert.Equal(["VIGNETTE_B:CREW_01", "VIGNETTE_B:CREW_02", "VIGNETTE_B:CREW_03"], third);
        Assert.Equal(["VIGNETTE_A:CREW_01", "VIGNETTE_A:CREW_02", "VIGNETTE_A:CREW_03"], fourth);
    }

    // Review focus 4.
    [Fact]
    public void Leaving_the_itinerary_applies_each_unanswered_default_once_and_vignettes_do_not_hold_it()
    {
        VignetteDef word = Rested("VIGNETTE_WORD", Option("speak", new StoryletEffect(Morale: 3, MoraleOn: "subject")), Option("quiet", Loyalty(-1, "subject")));
        var h = new Harness(Vignettes(2, 6, word));
        h.To(CyclePhase.Itinerary);
        Assert.Equal(["2:VIGNETTE_WORD:CREW_01", "2:VIGNETTE_WORD:CREW_02"], h.Storylets.Vignettes.Select(v => v.OfferId));

        List<IEvent> answered = h.Send(new AnswerVignette("2:VIGNETTE_WORD:CREW_01", "speak"));
        List<IEvent> left = h.Send(new AdvancePhase());
        List<IEvent> later = h.To(CyclePhase.Aftermath);

        Assert.Equal(new VignetteAnswered("2:VIGNETTE_WORD:CREW_01", "VIGNETTE_WORD", "CREW_01", "speak", false), answered[^1]);
        Assert.Contains(new MoraleChanged("CREW_01", 50, 53, MoraleCauses.Answer), answered);
        Assert.Contains(new PhaseEntered(2, CyclePhase.Orders), left);
        Assert.Equal([new LoyaltyChanged("CREW_02", 50, 49), new VignetteAnswered("2:VIGNETTE_WORD:CREW_02", "VIGNETTE_WORD", "CREW_02", "quiet", true)], left.Where(e => e is LoyaltyChanged or VignetteAnswered));
        Assert.Empty(h.Storylets.Vignettes);
        Assert.DoesNotContain(later, e => e is VignetteAnswered);
    }

    [Fact]
    public void A_vignette_answer_is_refused_unless_it_waits_and_names_one_of_its_options()
    {
        VignetteDef word = Rested("VIGNETTE_WORD", Option("speak"), Option("quiet"));
        var h = new Harness(Vignettes(1, 6, word));
        h.To(CyclePhase.Itinerary);
        string before = JsonSerializer.Serialize(h.Storylets, SimJson.Options);

        Assert.Equal("That is not one of its answers.", h.Refused(new AnswerVignette("2:VIGNETTE_WORD:CREW_01", "shout")));
        Assert.Equal("That vignette is not waiting for an answer.", h.Refused(new AnswerVignette("2:VIGNETTE_WORD:CREW_02", "speak")));
        Assert.Equal(before, JsonSerializer.Serialize(h.Storylets, SimJson.Options));

        h.Send(new AnswerVignette("2:VIGNETTE_WORD:CREW_01", "speak"));

        Assert.Equal("That vignette is not waiting for an answer.", h.Refused(new AnswerVignette("2:VIGNETTE_WORD:CREW_01", "quiet")));
    }

    [Fact]
    public void A_dead_subjects_waiting_vignette_is_dropped_and_takes_no_default()
    {
        var h = new Harness(Vignettes(1, 6, Rested("VIGNETTE_WORD", Option("speak"), Option("quiet", Loyalty(-1, "subject")))));
        h.To(CyclePhase.Itinerary);

        h.End([], Died("CREW_01"));
        List<IEvent> left = h.Send(new AdvancePhase());

        Assert.Empty(h.Storylets.Vignettes);
        Assert.DoesNotContain(left, e => e is VignetteAnswered or LoyaltyChanged);
    }
}

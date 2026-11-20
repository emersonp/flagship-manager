using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;
using Sim.Expressions;
using Sim.Storylets;
using Sim.Traits;

namespace Sim.Tests;

public class StoryletsModuleTests
{
    internal const string Encounter = "ENCOUNTER_01";

    internal sealed record EndMission(MissionResult Result) : ICommand;

    // Stands in for the missions module's start of a mission (1e part three): the team as launched.
    internal sealed record StartMission(IReadOnlyList<string> CrewIds) : ICommand;

    internal sealed record PassDays(int Days) : ICommand;

    // Cycle, crew, traits, and storylets on one bus; a test command stands in for the missions module's end of a mission.
    internal sealed class Harness
    {
        public Harness(ContentPack pack, ulong seed = 1)
        {
            State = new LegacyState { LegacyId = Guid.Empty, Seed = seed };
            Bus = Build(pack);
            Send(new FoundLegacy());
        }

        public EventBus Bus { get; private set; }

        public LegacyState State { get; }

        public StoryletsState Storylets => State.Module<StoryletsState>(StoryletsModule.Name);

        public TraitsState Traits => State.Module<TraitsState>(TraitsModule.Name);

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

        // Days pass with no turn of the cycle: what falls due on them, alone.
        public List<IEvent> PassDays(int days)
        {
            return Send(new PassDays(days));
        }

        public List<IEvent> Launch(params string[] crew)
        {
            return Send(new StartMission(crew));
        }

        public List<IEvent> End(MissionTrait[] traits, params CrewResult[] crew)
        {
            return End(MissionOutcome.PartialSuccess, traits, crew);
        }

        public List<IEvent> End(MissionOutcome outcome, MissionTrait[] traits, params CrewResult[] crew)
        {
            return Send(new EndMission(new MissionResult(outcome, 1, 2, 0, 0, crew, traits)));
        }

        public IHistoryView History => Bus.View<IHistoryView>(State);

        // A mission that changes these traits, then the next cycle's itinerary, where its storylets arrive; the events of both.
        public List<IEvent> Mission(params MissionTrait[] traits)
        {
            var events = To(CyclePhase.Resolution);
            events.AddRange(End(traits));
            events.AddRange(To(CyclePhase.Itinerary));
            return events;
        }

        public int Trait(string trait, string? crewId = null, string? targetId = null)
        {
            return Traits.Traits.FirstOrDefault(t => t.Trait == trait && t.CrewId == crewId && t.TargetId == targetId)?.Value ?? 0;
        }

        public List<string> Waiting => Storylets.Waiting.Select(w => w.OfferId).ToList();

        // The cast of the waiting storylet with this id, as "slot=CREW, ...".
        public string CastOf(string storyletId)
        {
            return Show(Storylets.Waiting.Single(w => w.StoryletId == storyletId).Cast);
        }

        private static EventBus Build(ContentPack pack)
        {
            var bus = new EventBus();
            CycleModule.Register(bus, pack);
            CrewModule.Register(bus, pack);
            TraitsModule.Register(bus, pack);
            StoryletsModule.Register(bus, pack);
            bus.Handle<EndMission>((command, context) => context.Publish(new MissionEnded("MISSION_1", "OBJECTIVE_1", command.Result)));
            bus.Handle<PassDays>((command, context) =>
            {
                for (int day = context.State.Day + 1; day <= context.State.Day + command.Days; day++)
                {
                    context.Publish(new DayReached(day));
                }
            });
            bus.Handle<StartMission>((command, context) => context.Publish(new MissionStarted("MISSION_1", "OBJECTIVE_1", "OBJECTIVE_TEMPLATE_1", command.CrewIds, command.CrewIds[0])));
            return bus;
        }
    }

    internal static ContentPack Pack(int perCycle, params StoryletDef[] storylets)
    {
        // No vignettes: their arrivals and effects belong to their own tests.
        return TestContent.Pack with { Storylets = storylets, StoryletsPerCycle = perCycle, Vignettes = [] };
    }

    internal static CastSlot Slot(string name, string expression)
    {
        return new CastSlot(name, new Expr(expression));
    }

    internal static StoryletDef Ship(string id, int stakes, string requires, params StoryletOption[] options)
    {
        return new StoryletDef(id, stakes, [], new Expr(requires), options);
    }

    internal static StoryletDef Crew(string id, int stakes, string subject, params StoryletOption[] options)
    {
        return new StoryletDef(id, stakes, [Slot("subject", subject)], null, options);
    }

    internal static StoryletOption Option(string id, params StoryletEffect[] effects)
    {
        return new StoryletOption(id, id.ToUpperInvariant(), effects);
    }

    internal static StoryletEffect Set(string trait, int value, string on = "")
    {
        return new StoryletEffect(Trait: new TraitChange(trait, value, true, on));
    }

    internal static StoryletEffect Add(string trait, int amount, string on = "")
    {
        return new StoryletEffect(Trait: new TraitChange(trait, amount, false, on));
    }

    internal static StoryletEffect Loyalty(int amount, string on)
    {
        return new StoryletEffect(Loyalty: amount, LoyaltyOn: on);
    }

    internal static MissionTrait Raise(string trait, int amount, string? crewId = null, string encounter = Encounter)
    {
        return new MissionTrait(trait, amount, false, crewId, encounter);
    }

    internal static MissionTrait Fix(string trait, int value, string? crewId = null, string encounter = Encounter)
    {
        return new MissionTrait(trait, value, true, crewId, encounter);
    }

    internal static CrewResult Died(string id)
    {
        return new CrewResult(id, MemberStatus.Dead, 0, new Dictionary<string, double>(), 0);
    }

    // A cast as "slot=CREW, ...", in reading order; "" for the ship.
    internal static string Show(IReadOnlyDictionary<string, string> cast)
    {
        return string.Join(", ", cast.Select(c => $"{c.Key}={c.Value}"));
    }

    // The ship storylet of the fixture: trait_a at 1, cleared or moved on to 2.
    internal static readonly StoryletDef Quirk = Ship("STORYLET_QUIRK", 2, "ship.trait.trait_a == 1", Option("clear", Set("trait_a", 0)), Option("ride", Set("trait_a", 2)));

    // A crew storylet: trait_b at 1 or more on someone.
    internal static readonly StoryletDef Shaken = Crew("STORYLET_SHAKEN", 3, "subject.trait.trait_b >= 1", Option("calm", Add("trait_b", -1)), Option("ignore", Set("trait_b", 0)));

    [Fact]
    public void A_storylet_whose_expressions_hold_is_offered_when_the_itinerary_opens()
    {
        var h = new Harness(Pack(2, Quirk));
        h.To(CyclePhase.Resolution);

        List<IEvent> ended = h.End([Raise("trait_a", 1)]);
        List<IEvent> itinerary = h.To(CyclePhase.Itinerary);

        Assert.DoesNotContain(ended, e => e is StoryletOffered);
        StoryletOffered offered = Assert.Single(itinerary.OfType<StoryletOffered>());
        Assert.Equal(("2:STORYLET_QUIRK", "STORYLET_QUIRK", "", (string?)Encounter), (offered.OfferId, offered.StoryletId, Show(offered.Cast), offered.EncounterId));
        WaitingStorylet waiting = Assert.Single(h.Storylets.Waiting);
        Assert.Equal(("2:STORYLET_QUIRK", "STORYLET_QUIRK", "", (string?)Encounter), (waiting.OfferId, waiting.StoryletId, Show(waiting.Cast), waiting.EncounterId));
    }

    // A storylet with no cast reads requires on the ship once, and draws from no cast stream.
    [Fact]
    public void A_ship_storylet_casts_no_one()
    {
        var h = new Harness(Pack(2, Quirk, Shaken));

        h.Mission(Raise("trait_a", 1), Raise("trait_b", 1, "CREW_02"));

        Assert.Equal("", h.CastOf("STORYLET_QUIRK"));
        Assert.Equal("subject=CREW_02", h.CastOf("STORYLET_SHAKEN"));
        Assert.Contains(h.State.RngStates.Keys, k => k == "2:cast:STORYLET_SHAKEN:2");
        Assert.DoesNotContain(h.State.RngStates.Keys, k => k.Contains("STORYLET_QUIRK", StringComparison.Ordinal));
    }

    [Fact]
    public void A_storylet_for_the_ship_ignores_the_same_trait_on_a_crew_member_and_the_reverse()
    {
        var h = new Harness(Pack(4, Quirk, Shaken));

        List<IEvent> events = h.Mission(Raise("trait_a", 1, "CREW_01"), Raise("trait_b", 1));

        Assert.DoesNotContain(events, e => e is StoryletOffered);
        Assert.Empty(h.Waiting);
    }

    // Review focus 2: one cast a cycle. Two crew hold it: one is cast now, the other once the first is answered.
    [Fact]
    public void A_storylet_arrives_at_most_once_a_cycle()
    {
        var h = new Harness(Pack(4, Shaken));

        List<IEvent> first = h.Mission(Raise("trait_b", 1, "CREW_03"), Raise("trait_b", 1, "CREW_01"));

        StoryletOffered offered = Assert.Single(first.OfType<StoryletOffered>());
        Assert.Equal("2:STORYLET_SHAKEN", offered.OfferId);
        string cast = offered.Cast["subject"];
        Assert.Contains(cast, new[] { "CREW_01", "CREW_03" });

        h.Send(new AnswerStorylet("2:STORYLET_SHAKEN", "calm"));
        List<IEvent> next = h.To(CyclePhase.Itinerary);

        StoryletOffered again = Assert.Single(next.OfType<StoryletOffered>());
        Assert.Equal(("3:STORYLET_SHAKEN", cast == "CREW_01" ? "CREW_03" : "CREW_01"), (again.OfferId, again.Cast["subject"]));
    }

    [Fact]
    public void At_most_the_cycles_limit_are_offered_highest_stakes_first_and_the_rest_next_cycle()
    {
        StoryletDef sore = Crew("STORYLET_SORE", 1, "subject.trait.trait_j >= 1", Option("rest", Set("trait_j", 0)), Option("work", Set("trait_j", 0)));
        var h = new Harness(Pack(2, Quirk, sore, Shaken));

        List<IEvent> first = h.Mission(Raise("trait_a", 1), Raise("trait_j", 3, "CREW_02"), Raise("trait_b", 1, "CREW_01"));

        Assert.Equal(["2:STORYLET_SHAKEN", "2:STORYLET_QUIRK"], first.OfType<StoryletOffered>().Select(o => o.OfferId));
        Assert.Equal(["2:STORYLET_SHAKEN", "2:STORYLET_QUIRK"], h.Waiting);

        h.Send(new AnswerStorylet("2:STORYLET_SHAKEN", "calm"));
        h.Send(new AnswerStorylet("2:STORYLET_QUIRK", "clear"));
        List<IEvent> next = h.To(CyclePhase.Itinerary);

        Assert.Equal(3, h.State.Cycle);
        Assert.Equal(["3:STORYLET_SORE"], next.OfType<StoryletOffered>().Select(o => o.OfferId));
    }

    // Review focus 3: the cast draws only from its own stream, so the same seed casts the same crew member; another seed may not.
    [Fact]
    public void Casting_is_the_same_for_the_same_seed()
    {
        MissionTrait[] everyone = [.. Enumerable.Range(1, 8).Select(n => Raise("trait_b", 1, $"CREW_0{n}"))];
        string CastFor(ulong seed)
        {
            var h = new Harness(Pack(2, Shaken), seed);
            h.Mission(everyone);
            return h.CastOf("STORYLET_SHAKEN");
        }

        Assert.Equal(CastFor(1), CastFor(1));
        Assert.True(Enumerable.Range(1, 10).Select(s => CastFor((ulong)s)).Distinct().Count() > 1, "Ten seeds all cast the same crew member: the order is not drawn.");
    }

    [Fact]
    public void A_slot_is_never_bound_twice()
    {
        var two = new StoryletDef("STORYLET_TWO", 1, [Slot("subject", "subject.trait.trait_b >= 1"), Slot("target", "target.trait.trait_b >= 1")], null,
            [Option("calm", Set("trait_b", 0)), Option("other", Set("trait_b", 0))]);
        var h = new Harness(Pack(2, two));

        h.Mission(Raise("trait_b", 3, "CREW_04"));

        Assert.Empty(h.Waiting);

        h.To(CyclePhase.Resolution);
        h.End([Raise("trait_b", 3, "CREW_06")]);
        h.To(CyclePhase.Itinerary);

        Assert.Contains(h.CastOf("STORYLET_TWO"), new[] { "subject=CREW_04, target=CREW_06", "subject=CREW_06, target=CREW_04" });
    }

    // Design 4.4: "no cast" and "out of tries" are told apart; every expression read is a try, requires included.
    [Fact]
    public void Out_of_tries_is_told_apart_from_no_cast()
    {
        ContentPack pack = Pack(2, Shaken, new StoryletDef("STORYLET_TWO", 1, [Slot("subject", "subject.fit"), Slot("target", "target.fit")], new Expr("subject.trait.trait_b > target.trait.trait_b"),
            [Option("calm", Set("trait_b", 0)), Option("other", Set("trait_b", 0))]));
        var casting = new Casting(pack);
        string[] roster = ["CREW_01", "CREW_02", "CREW_03"];
        var state = new Source(new Dictionary<string, int> { ["CREW_03"] = 1 });
        CastResult Cast(string id, int tries)
        {
            return casting.Cast(id, state, roster, () => RngStream.For(1, 2, $"cast:{id}:2"), tries, _ => false);
        }

        CastResult one = Cast("STORYLET_SHAKEN", 50);
        Assert.Equal((CastOutcome.Cast, "subject=CREW_03"), (one.Outcome, Show(one.Cast!)));

        var nobody = new Source([]);
        CastResult none = casting.Cast("STORYLET_SHAKEN", nobody, roster, () => RngStream.For(1, 2, "cast:STORYLET_SHAKEN:2"), 50, _ => false);
        CastResult short_ = casting.Cast("STORYLET_SHAKEN", nobody, roster, () => RngStream.For(1, 2, "cast:STORYLET_SHAKEN:2"), 2, _ => false);
        Assert.Equal((CastOutcome.NoCast, 3), (none.Outcome, none.Tries));
        Assert.Equal((CastOutcome.OutOfTries, 2), (short_.Outcome, short_.Tries));
        Assert.Null(short_.Cast);

        // Two slots and requires: each subject is a read, and each target and requires on the pair two more, so 3 x (1 + 2 x 2) = 15
        // at most. Two tries reach a subject and a target and stop before requires.
        CastResult pair = Cast("STORYLET_TWO", 50);
        Assert.Equal(CastOutcome.Cast, pair.Outcome);
        Assert.Equal("CREW_03", pair.Cast!["subject"]);
        Assert.True(pair.Tries <= 15);
        Assert.Equal((CastOutcome.OutOfTries, 2), (Cast("STORYLET_TWO", 2).Outcome, Cast("STORYLET_TWO", 2).Tries));
    }

    [Fact]
    public void An_answer_applies_its_effects_in_order_and_the_storylet_does_not_return()
    {
        StoryletDef storylet = Ship("STORYLET_ORDER", 1, "ship.trait.trait_a == 1", Option("first", Set("trait_a", 5), Add("trait_a", -5), Add("trait_b", 1, "ship")), Option("second", Set("trait_a", 0)));
        var h = new Harness(Pack(2, storylet));
        h.Mission(Raise("trait_a", 1));

        List<IEvent> events = h.Send(new AnswerStorylet("2:STORYLET_ORDER", "first"));

        Assert.Equal(
            [
                new TraitChanged("trait_a", null, null, 1, 5, TraitCauses.Answer, null),
                new TraitChanged("trait_a", null, null, 5, 0, TraitCauses.Answer, null),
                new TraitChanged("trait_b", null, null, 0, 1, TraitCauses.Answer, null),
            ],
            events.Take(3));
        var answered = Assert.IsType<StoryletAnswered>(events[3]);
        Assert.Equal(("2:STORYLET_ORDER", "STORYLET_ORDER", "", "first"), (answered.OfferId, answered.StoryletId, Show(answered.Cast), answered.OptionId));
        Assert.Equal(4, events.Count);
        Assert.Empty(h.Waiting);
        Assert.Equal((0, 1), (h.Trait("trait_a"), h.Trait("trait_b")));
        StoredTrait stored = h.Traits.Traits.Single();
        Assert.Equal((h.State.Day, 2, (string?)null), (stored.ChangedAtDay, stored.ChangedAtCycle, stored.EncounterId));

        List<IEvent> later = h.To(CyclePhase.Itinerary);
        later.AddRange(h.To(CyclePhase.Itinerary));

        Assert.DoesNotContain(later, e => e is StoryletOffered);
    }

    [Fact]
    public void An_answer_that_moves_the_trait_on_lets_another_storylet_require_the_new_value()
    {
        StoryletDef known = Ship("STORYLET_KNOWN", 1, "ship.trait.trait_a == 2", Option("fix", Set("trait_a", 0)), Option("drop", Set("trait_a", 3)));
        var h = new Harness(Pack(2, Quirk, known));
        h.Mission(Raise("trait_a", 1));

        h.Send(new AnswerStorylet("2:STORYLET_QUIRK", "ride"));
        List<IEvent> next = h.To(CyclePhase.Itinerary);

        Assert.Equal(2, h.Trait("trait_a"));
        StoredTrait stored = h.Traits.Traits.Single();
        Assert.Equal(Encounter, stored.EncounterId);
        StoryletOffered offered = Assert.Single(next.OfType<StoryletOffered>());
        Assert.Equal(("3:STORYLET_KNOWN", (string?)Encounter), (offered.OfferId, offered.EncounterId));
    }

    [Fact]
    public void Loyalty_on_crew_reaches_every_living_member_and_is_clamped()
    {
        StoryletDef storylet = Crew("STORYLET_LOYAL", 1, "subject.trait.trait_b >= 1", Option("everyone", Loyalty(-3, "crew"), Set("trait_b", 0), Loyalty(2, "subject")), Option("other", Set("trait_b", 0)));
        var h = new Harness(Pack(2, storylet));
        h.To(CyclePhase.Resolution);
        h.End([Raise("trait_b", 1, "CREW_02")], Died("CREW_08"));
        h.To(CyclePhase.Itinerary);
        h.Crew.Members.Single(m => m.Id == "CREW_01").Loyalty = 1;
        h.Crew.Members.Single(m => m.Id == "CREW_02").Loyalty = 99;

        List<IEvent> events = h.Send(new AnswerStorylet("2:STORYLET_LOYAL", "everyone"));

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
        h.Mission(Raise("trait_a", 1));
        string before = JsonSerializer.Serialize(h.Storylets, SimJson.Options);

        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("2:STORYLET_OTHER", "clear")));
        Assert.Equal("That is not one of its answers.", h.Refused(new AnswerStorylet("2:STORYLET_QUIRK", "option_z")));
        Assert.Equal(before, JsonSerializer.Serialize(h.Storylets, SimJson.Options));

        h.Send(new AnswerStorylet("2:STORYLET_QUIRK", "clear"));
        string after = JsonSerializer.Serialize(h.Storylets, SimJson.Options);

        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("2:STORYLET_QUIRK", "clear")));
        Assert.Equal(after, JsonSerializer.Serialize(h.Storylets, SimJson.Options));
    }

    [Fact]
    public void The_itinerary_cannot_be_left_while_a_storylet_waits()
    {
        var h = new Harness(Pack(2, Quirk));
        h.Mission(Raise("trait_a", 1));

        Assert.Equal("A storylet is waiting for an answer.", h.Refused(new AdvancePhase()));
        Assert.Equal(CyclePhase.Itinerary, h.State.Phase);

        h.Send(new AnswerStorylet("2:STORYLET_QUIRK", "clear"));
        h.Send(new AdvancePhase());

        Assert.Equal(CyclePhase.Orders, h.State.Phase);
    }

    [Fact]
    public void A_storylet_still_waits_in_a_cycle_where_no_team_was_sent()
    {
        StoryletDef second = Ship("STORYLET_SECOND", 1, "ship.trait.trait_b >= 1", Option("clear", Set("trait_b", 0)), Option("also", Set("trait_b", 0)));
        var h = new Harness(Pack(1, Quirk, second));
        h.Mission(Raise("trait_a", 1), Raise("trait_b", 1));
        Assert.Equal(["2:STORYLET_QUIRK"], h.Waiting);
        h.Send(new AnswerStorylet("2:STORYLET_QUIRK", "clear"));

        // Cycle 2: nobody is sent, and no mission ends.
        List<IEvent> events = h.To(CyclePhase.Itinerary);

        Assert.DoesNotContain(events, e => e is MissionEnded);
        Assert.Equal(["3:STORYLET_SECOND"], h.Waiting);
        Assert.Equal("A storylet is waiting for an answer.", h.Refused(new AdvancePhase()));
        h.Send(new AnswerStorylet("3:STORYLET_SECOND", "clear"));
        h.Send(new AdvancePhase());
        Assert.Equal(CyclePhase.Orders, h.State.Phase);
    }

    [Fact]
    public void A_waiting_storylet_the_pack_no_longer_has_is_withdrawn()
    {
        var h = new Harness(Pack(2, Quirk));
        h.Mission(Raise("trait_a", 1));
        h.Rebuild(Pack(2));

        // It can no longer be answered, and it does not hold the legacy in the itinerary.
        Assert.Equal("That storylet is not waiting for an answer.", h.Refused(new AnswerStorylet("2:STORYLET_QUIRK", "clear")));
        h.Send(new AdvancePhase());
        List<IEvent> next = h.To(CyclePhase.Itinerary);

        StoryletWithdrawn withdrawn = Assert.Single(next.OfType<StoryletWithdrawn>());
        Assert.Equal(("2:STORYLET_QUIRK", "STORYLET_QUIRK", ""), (withdrawn.OfferId, withdrawn.StoryletId, Show(withdrawn.Cast)));
        Assert.Empty(h.Waiting);
        Assert.Equal(1, h.Trait("trait_a"));
    }

    // Review focus 5: a dead cast member withdraws the offer, whichever slot they held.
    [Theory]
    [InlineData("subject")]
    [InlineData("target")]
    [InlineData("witness")]
    public void Any_cast_members_death_withdraws_it(string slot)
    {
        var three = new StoryletDef("STORYLET_THREE", 2, [Slot("subject", "subject.trait.trait_b >= 1"), Slot("target", "target.trait.trait_j >= 1"), Slot("witness", "witness.trait.trait_c >= 1")], null,
            [Option("calm", Set("trait_b", 0)), Option("other", Set("trait_b", 0))]);
        var glance = new VignetteDef("VIGNETTE_THREE", three.Cast, null, [], [Option("a"), Option("b")], "a");
        var h = new Harness(Pack(2, three) with { Vignettes = [glance] });
        h.Mission(Raise("trait_b", 1, "CREW_02"), Raise("trait_j", 1, "CREW_05"), Raise("trait_c", 1, "CREW_07"));
        Assert.Equal("subject=CREW_02, target=CREW_05, witness=CREW_07", h.CastOf("STORYLET_THREE"));
        Assert.Single(h.Storylets.Vignettes);
        string dead = h.Storylets.Waiting.Single().Cast[slot];

        List<IEvent> events = h.End([], Died(dead));

        StoryletWithdrawn withdrawn = Assert.Single(events.OfType<StoryletWithdrawn>());
        Assert.Equal(("2:STORYLET_THREE", "subject=CREW_02, target=CREW_05, witness=CREW_07"), (withdrawn.OfferId, Show(withdrawn.Cast)));
        Assert.Empty(h.Waiting);
        Assert.Empty(h.Storylets.Vignettes);
        Assert.DoesNotContain(h.Storylets.Arrivals, a => a.Cast.ContainsValue(dead));
    }

    // 1e part two's stats and the history, read in expressions (design 4.3).
    private static CrewResult Home(string id, int injury = 0)
    {
        return new CrewResult(id, MemberStatus.Active, injury, new Dictionary<string, double>(), 0);
    }

    [Fact]
    public void An_expression_reads_the_subjects_morale()
    {
        StoryletDef low = Crew("STORYLET_LOW", 1, "subject.trait.trait_b >= 1 && subject.morale <= 25", Option("calm", Set("trait_b", 0)), Option("ignore", Set("trait_b", 0)));
        var h = new Harness(Pack(8, low));
        h.Crew.Members.Single(m => m.Id == "CREW_02").Morale = 20;

        h.Mission(Raise("trait_b", 1, "CREW_01"), Raise("trait_b", 1, "CREW_02"));

        Assert.Equal("subject=CREW_02", h.CastOf("STORYLET_LOW"));
    }

    // Review focus 1 of part two, and the author's decision of 2026-11-18: the dead hold no kind; every living crew member lost a
    // crewmate, and those who came home from the mission also lost a teammate.
    [Fact]
    public void Each_history_kind_holds_for_the_right_crew()
    {
        var h = new Harness(Pack(100));
        h.To(CyclePhase.Resolution);
        h.End([], Home("CREW_01"), Home("CREW_02", injury: 1), Died("CREW_03"));

        h.To(CyclePhase.Itinerary);

        List<string> Holding(string kind)
        {
            return Enumerable.Range(1, 8).Select(n => $"CREW_0{n}").Where(id => h.History.Count(id, kind, 1) > 0).ToList();
        }

        Assert.Equal(["CREW_01", "CREW_02"], Holding(HistoryKinds.Sent));
        Assert.Equal(["CREW_02"], Holding(HistoryKinds.Hurt));
        Assert.Equal(["CREW_04", "CREW_05", "CREW_06", "CREW_07", "CREW_08"], Holding(HistoryKinds.Rested));
        Assert.Equal(["CREW_01", "CREW_02", "CREW_04", "CREW_05", "CREW_06", "CREW_07", "CREW_08"], Holding(HistoryKinds.LostCrewmate));
        Assert.Equal(["CREW_01", "CREW_02"], Holding(HistoryKinds.LostTeammate));
    }

    [Fact]
    public void A_mission_with_no_death_loses_no_one_a_teammate()
    {
        var h = new Harness(Pack(100));
        h.To(CyclePhase.Resolution);
        h.End([], Home("CREW_01"), Home("CREW_02", injury: 1));
        h.To(CyclePhase.Itinerary);

        Assert.Equal(0, h.History.Count("CREW_01", HistoryKinds.LostTeammate, 1));
        Assert.Equal(0, h.History.Count("CREW_04", HistoryKinds.LostCrewmate, 1));
    }

    [Fact]
    public void In_a_cycle_with_no_team_everyone_fit_rested_and_nobody_was_sent()
    {
        var h = new Harness(Pack(100));

        h.To(CyclePhase.Itinerary);

        Assert.All(h.Crew.Members.Select(m => m.Id), id => Assert.Equal((1, 0), (h.History.Count(id, HistoryKinds.Rested, 1), h.History.Count(id, HistoryKinds.Sent, 1))));
    }

    // 1e part four (design 4.8): the history. It replaces the last cycle's record, so the last cycle is no longer forgotten when
    // the next orders open.
    [Fact]
    public void Last_reads_the_history()
    {
        StoryletDef stayed = Crew("STORYLET_STAYED", 1, "subject.trait.trait_j >= 1 && !subject.history.last('sent')", Option("done", Set("trait_j", 0)), Option("other", Set("trait_j", 0)));
        var h = new Harness(Pack(100, stayed));
        h.To(CyclePhase.Resolution);
        h.End([Raise("trait_j", 3, "CREW_01"), Raise("trait_j", 3, "CREW_02")], Home("CREW_01"));
        h.To(CyclePhase.Itinerary);

        Assert.Equal("subject=CREW_02", h.CastOf("STORYLET_STAYED"));

        var later = new Harness(Pack(100));
        later.To(CyclePhase.Resolution);
        later.End([], Home("CREW_01"));
        later.To(CyclePhase.Orders);

        Assert.Equal(1, later.History.Count("CREW_01", HistoryKinds.Sent, 1));
        Assert.Equal(0, later.History.Count("CREW_02", HistoryKinds.Sent, 1));
        Assert.Equal(1, later.History.Count("CREW_02", HistoryKinds.Rested, 1));
    }

    [Fact]
    public void History_keeps_the_last_N_cycles()
    {
        var h = new Harness(Pack(100) with { HistoryCycles = 3 });
        h.To(CyclePhase.Resolution);
        h.End([], Home("CREW_01", injury: 1));
        for (int i = 0; i < 5; i++)
        {
            h.To(CyclePhase.Itinerary);
        }

        // Five cycles have ended: the sixth is in its itinerary. Cycle 1's mission has fallen out; cycles 3 to 5 are kept.
        Assert.Equal(6, h.State.Cycle);
        Assert.Equal([3, 4, 5], h.Storylets.History.Select(c => c.Cycle));
        Assert.Equal(3, h.History.Count("CREW_01", HistoryKinds.Rested, 3));
        Assert.Equal(1, h.History.Count("CREW_01", HistoryKinds.Rested, 1));
        Assert.Equal(0, h.History.Count("CREW_01", HistoryKinds.Hurt, 3));
        Assert.Equal(0, h.History.Count("CREW_01", HistoryKinds.Sent, 3));
    }

    [Theory]
    [InlineData(MissionOutcome.Loss, true)]
    [InlineData(MissionOutcome.Withdrawal, true)]
    [InlineData(MissionOutcome.Incomplete, true)]
    [InlineData(MissionOutcome.PartialSuccess, false)]
    [InlineData(MissionOutcome.FullSuccess, false)]
    public void Lost_mission_means_failed_or_abandoned_not_a_death(MissionOutcome outcome, bool lost)
    {
        var h = new Harness(Pack(100));
        h.To(CyclePhase.Resolution);
        h.End(outcome, [], Home("CREW_01"), Home("CREW_02", injury: 1), Died("CREW_03"));
        h.To(CyclePhase.Itinerary);

        Assert.Equal(lost ? 1 : 0, h.History.Count("CREW_01", HistoryKinds.LostMission, 1));
        Assert.Equal(lost ? 1 : 0, h.History.Count("CREW_02", HistoryKinds.LostMission, 1));
        Assert.Equal(0, h.History.Count("CREW_04", HistoryKinds.LostMission, 1));
        Assert.Equal(1, h.History.Count("CREW_01", HistoryKinds.LostCrewmate, 1));
        Assert.Equal(1, h.History.Count("CREW_04", HistoryKinds.LostCrewmate, 1));
        Assert.Equal(1, h.History.Count("CREW_01", HistoryKinds.LostTeammate, 1));
        Assert.Equal(0, h.History.Count("CREW_04", HistoryKinds.LostTeammate, 1));
    }

    [Fact]
    public void The_dead_drop_out()
    {
        var h = new Harness(Pack(100));
        h.To(CyclePhase.Resolution);
        h.End([], Home("CREW_03", injury: 1), Home("CREW_01"));
        h.To(CyclePhase.Itinerary);
        Assert.Equal(1, h.History.Count("CREW_03", HistoryKinds.Hurt, 1));

        h.To(CyclePhase.Resolution);
        h.End([], Died("CREW_03"), Home("CREW_01"));
        h.To(CyclePhase.Itinerary);

        Assert.Equal(0, h.History.Count("CREW_03", HistoryKinds.Sent, 20));
        Assert.DoesNotContain(h.Storylets.History, c => c.Crew.ContainsKey("CREW_03"));
        Assert.Equal(2, h.History.Count("CREW_01", HistoryKinds.Sent, 20));
    }

    [Fact]
    public void A_morale_effect_reaches_the_crew_module()
    {
        StoryletDef cheer = Crew("STORYLET_CHEER", 1, "subject.trait.trait_b >= 1",
            Option("cheer", Set("trait_b", 0), new StoryletEffect(Morale: 3, MoraleOn: "subject"), new StoryletEffect(Morale: -1, MoraleOn: "crew")), Option("other", Set("trait_b", 0)));
        var h = new Harness(Pack(2, cheer));
        h.Mission(Raise("trait_b", 1, "CREW_02"));

        List<IEvent> events = h.Send(new AnswerStorylet(h.Waiting.Single(), "cheer"));

        Assert.Contains(new MoraleChanged("CREW_02", 50, 53, MoraleCauses.Answer), events);
        Assert.Equal(52, h.Crew.Members.Single(m => m.Id == "CREW_02").Morale);
        Assert.Equal(49, h.Crew.Members.Single(m => m.Id == "CREW_05").Morale);
    }

    // 1e part two (design 4.3): vignettes arrive when the itinerary opens and leave when it closes; cast from part four.
    private static ContentPack Vignettes(int perCycle, int cooldown, params VignetteDef[] vignettes)
    {
        return TestContent.Pack with { Storylets = [], Vignettes = vignettes, VignettesPerCycle = perCycle, VignetteCooldown = cooldown };
    }

    private static VignetteDef Rested(string id, params StoryletOption[] options)
    {
        CastSlot[] cast = [Slot("subject", "subject.history.last('rested')")];
        return options.Length == 0
            ? new VignetteDef(id, cast, null, [new StoryletEffect(Morale: 1, MoraleOn: "subject")], [], null)
            : new VignetteDef(id, cast, null, [], options, options[^1].Id);
    }

    private static List<string> Arrived(IEnumerable<IEvent> events)
    {
        return events.OfType<VignetteArrived>().Select(a => $"{a.VignetteId}:{(a.Cast.Count == 0 ? "ship" : string.Join(":", a.Cast.Values))}").ToList();
    }

    [Fact]
    public void A_vignette_with_effects_applies_them_when_it_arrives_and_waits_for_nothing()
    {
        var glance = new VignetteDef("VIGNETTE_GLANCE", [], new Expr("ship.trait.trait_a >= 1"), [new StoryletEffect(Morale: 2, MoraleOn: "crew")], [], null);
        var h = new Harness(Vignettes(3, 6, glance));

        List<IEvent> events = h.Mission(Raise("trait_a", 1));

        int at = events.FindIndex(e => e is VignetteArrived);
        var arrived = (VignetteArrived)events[at];
        Assert.Equal(("2:VIGNETTE_GLANCE", "VIGNETTE_GLANCE", ""), (arrived.OfferId, arrived.VignetteId, Show(arrived.Cast)));
        Assert.Equal(8, events.Skip(at + 1).TakeWhile(e => e is MoraleChanged).Count());
        var answered = Assert.IsType<VignetteAnswered>(events[at + 9]);
        Assert.Equal(("2:VIGNETTE_GLANCE", (string?)null, false), (answered.OfferId, answered.OptionId, answered.Defaulted));
        Assert.Empty(h.Storylets.Vignettes);
        Assert.Equal(52, h.Crew.Members[0].Morale);
    }

    // Review focus 3 of part two, for a cast: the cooldown is for one cast, and casting passes over a cast that is cooling. With one
    // a cycle, no crew member has it twice within six cycles.
    [Fact]
    public void A_vignette_in_its_cooldown_does_not_arrive_with_that_cast_only()
    {
        var h = new Harness(Vignettes(1, 6, Rested("VIGNETTE_REST")));
        var arrived = new List<string>();

        for (int cycle = 2; cycle <= 12; cycle++)
        {
            arrived.AddRange(Arrived(h.To(CyclePhase.Itinerary)));
        }

        Assert.Equal(11, arrived.Count);
        for (int i = 0; i < arrived.Count; i++)
        {
            Assert.DoesNotContain(arrived[i], arrived.Skip(Math.Max(0, i - 5)).Take(i - Math.Max(0, i - 5)));
        }
    }

    // Design 4.5: part two's order (the one away longest first) is replaced by salience. With no stakes and one clause each, the
    // two tie at 1 and the draw picks; the one that came then has 1 - 3, so the other comes next.
    [Fact]
    public void Vignettes_arrive_by_salience_and_the_one_that_just_came_falls_back()
    {
        var h = new Harness(Vignettes(1, 0, Rested("VIGNETTE_A"), Rested("VIGNETTE_B")));

        VignetteArrived second = Assert.Single(h.To(CyclePhase.Itinerary).OfType<VignetteArrived>());
        VignetteArrived third = Assert.Single(h.To(CyclePhase.Itinerary).OfType<VignetteArrived>());

        Assert.NotEqual(second.VignetteId, third.VignetteId);
        Assert.Equal((1, 1), (second.Salience, third.Salience));

        var both = new Harness(Vignettes(3, 0, Rested("VIGNETTE_A"), Rested("VIGNETTE_B")));
        Assert.Equal(["VIGNETTE_A", "VIGNETTE_B"], Arrived(both.To(CyclePhase.Itinerary)).Select(a => a.Split(':')[0]).Order(StringComparer.Ordinal));
    }

    // A vignette's own cooldown replaces vignettes.cooldown: global 2 brings it every other cycle.
    [Fact]
    public void A_vignettes_own_cooldown_replaces_the_packs()
    {
        var h = new Harness(Vignettes(3, 0, Rested("VIGNETTE_A") with { Cooldown = new Cooldown(0, 2) }));
        var cycles = new List<int>();

        for (int cycle = 2; cycle <= 7; cycle++)
        {
            if (h.To(CyclePhase.Itinerary).OfType<VignetteArrived>().Any())
            {
                cycles.Add(h.State.Cycle);
            }
        }

        Assert.Equal([2, 4, 6], cycles);
    }

    // Review focus 4 of part two.
    [Fact]
    public void Leaving_the_itinerary_applies_each_unanswered_default_once_and_vignettes_do_not_hold_it()
    {
        VignetteDef word = Rested("VIGNETTE_WORD", Option("speak", new StoryletEffect(Morale: 3, MoraleOn: "subject")), Option("quiet", Loyalty(-1, "subject")));
        VignetteDef other = Rested("VIGNETTE_OTHER", Option("speak"), Option("quiet", Loyalty(-1, "subject")));
        var h = new Harness(Vignettes(2, 6, word, other));
        h.To(CyclePhase.Itinerary);
        Assert.Equal(["2:VIGNETTE_OTHER", "2:VIGNETTE_WORD"], h.Storylets.Vignettes.Select(v => v.OfferId).Order(StringComparer.Ordinal));
        string speaker = h.Storylets.Vignettes.Single(v => v.VignetteId == "VIGNETTE_WORD").Cast["subject"];
        string quiet = h.Storylets.Vignettes.Single(v => v.VignetteId == "VIGNETTE_OTHER").Cast["subject"];

        List<IEvent> answered = h.Send(new AnswerVignette("2:VIGNETTE_WORD", "speak"));
        List<IEvent> left = h.Send(new AdvancePhase());
        List<IEvent> later = h.To(CyclePhase.Aftermath);

        var spoke = Assert.IsType<VignetteAnswered>(answered[^1]);
        Assert.Equal(("2:VIGNETTE_WORD", $"subject={speaker}", "speak", false), (spoke.OfferId, Show(spoke.Cast), spoke.OptionId, spoke.Defaulted));
        Assert.Contains(new MoraleChanged(speaker, 50, 53, MoraleCauses.Answer), answered);
        Assert.Contains(new PhaseEntered(2, CyclePhase.Orders), left);
        Assert.Equal([new LoyaltyChanged(quiet, 50, 49)], left.OfType<LoyaltyChanged>());
        var defaulted = Assert.Single(left.OfType<VignetteAnswered>());
        Assert.Equal(("2:VIGNETTE_OTHER", "quiet", true), (defaulted.OfferId, defaulted.OptionId, defaulted.Defaulted));
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

        Assert.Equal("That is not one of its answers.", h.Refused(new AnswerVignette("2:VIGNETTE_WORD", "shout")));
        Assert.Equal("That vignette is not waiting for an answer.", h.Refused(new AnswerVignette("2:VIGNETTE_OTHER", "speak")));
        Assert.Equal(before, JsonSerializer.Serialize(h.Storylets, SimJson.Options));

        h.Send(new AnswerVignette("2:VIGNETTE_WORD", "speak"));

        Assert.Equal("That vignette is not waiting for an answer.", h.Refused(new AnswerVignette("2:VIGNETTE_WORD", "quiet")));
    }

    [Fact]
    public void A_dead_subjects_waiting_vignette_is_dropped_and_takes_no_default()
    {
        var h = new Harness(Vignettes(1, 6, Rested("VIGNETTE_WORD", Option("speak"), Option("quiet", Loyalty(-1, "subject")))));
        h.To(CyclePhase.Itinerary);

        h.End([], Died(h.Storylets.Vignettes.Single().Cast["subject"]));
        List<IEvent> left = h.Send(new AdvancePhase());

        Assert.Empty(h.Storylets.Vignettes);
        Assert.DoesNotContain(left, e => e is VignetteAnswered or LoyaltyChanged);
    }

    // --- Two slots (1e part three's pairs, cast from part four): the fixture defines trait_g on a pair; trait_j sits on crew ---

    private static MissionTrait Toward(string trait, int amount, string holderId, string targetId)
    {
        return new MissionTrait(trait, amount, false, holderId, Encounter, targetId);
    }

    private static StoryletDef Pair(string id, string target, string? requires, params StoryletOption[] options)
    {
        return new StoryletDef(id, 2, [Slot("subject", "subject.aboard"), Slot("target", target)], requires is null ? null : new Expr(requires), options);
    }

    private static StoryletEffect Toward(string trait, int value)
    {
        return new StoryletEffect(Trait: new TraitChange(trait, value, true, "", StoryletScopes.Target));
    }

    private const string Avoids = "subject.trait.trait_g.toward(target) >= 1";

    private static readonly StoryletDef Avoiding = Pair("STORYLET_PAIR", Avoids, null, Option("clear", Toward("trait_g", 0)), Option("keep", Toward("trait_g", 0)));

    [Fact]
    public void A_pair_storylet_is_cast_with_one_pair_a_cycle()
    {
        var h = new Harness(Pack(4, Avoiding));

        List<IEvent> events = h.Mission(Toward("trait_g", 1, "CREW_02", "CREW_03"), Toward("trait_g", 1, "CREW_01", "CREW_03"));

        StoryletOffered offered = Assert.Single(events.OfType<StoryletOffered>());
        Assert.Contains(Show(offered.Cast), new[] { "subject=CREW_01, target=CREW_03", "subject=CREW_02, target=CREW_03" });
        Assert.Equal(Encounter, offered.EncounterId);

        h.Send(new AnswerStorylet("2:STORYLET_PAIR", "clear"));
        StoryletOffered next = Assert.Single(h.To(CyclePhase.Itinerary).OfType<StoryletOffered>());
        Assert.NotEqual(Show(offered.Cast), Show(next.Cast));

        h.Send(new AnswerStorylet("3:STORYLET_PAIR", "clear"));
        Assert.DoesNotContain(h.To(CyclePhase.Itinerary), e => e is StoryletOffered);
    }

    [Fact]
    public void A_trait_not_on_a_pair_is_read_on_the_slot_it_names()
    {
        StoryletDef storylet = Pair("STORYLET_PAIR", Avoids, "subject.trait.trait_j >= 1", Option("clear", Toward("trait_g", 0)), Option("keep", Toward("trait_g", 0)));
        var h = new Harness(Pack(4, storylet));

        h.Mission(Toward("trait_g", 1, "CREW_02", "CREW_03"), Toward("trait_g", 1, "CREW_01", "CREW_03"), Raise("trait_j", 1, "CREW_02"));

        Assert.Equal("subject=CREW_02, target=CREW_03", h.CastOf("STORYLET_PAIR"));
    }

    [Fact]
    public void An_expression_reads_the_targets_stats_and_history()
    {
        StoryletDef low = Pair("STORYLET_LOW", $"{Avoids} && target.morale <= 25", null, Option("clear", Toward("trait_g", 0)), Option("keep", Toward("trait_g", 0)));
        StoryletDef hurt = Pair("STORYLET_HURT", $"{Avoids} && target.history.last('hurt')", null, Option("clear", Toward("trait_g", 0)), Option("keep", Toward("trait_g", 0)));
        var h = new Harness(Pack(4, low, hurt));
        h.To(CyclePhase.Resolution);
        h.End([Toward("trait_g", 1, "CREW_02", "CREW_03"), Toward("trait_g", 1, "CREW_03", "CREW_02")], new CrewResult("CREW_03", MemberStatus.Active, 1, new Dictionary<string, double>(), 0));
        h.Crew.Members.Single(m => m.Id == "CREW_02").Morale = 10;

        h.To(CyclePhase.Itinerary);

        Assert.Equal("subject=CREW_03, target=CREW_02", h.CastOf("STORYLET_LOW"));
        Assert.Equal("subject=CREW_02, target=CREW_03", h.CastOf("STORYLET_HURT"));
    }

    [Fact]
    public void On_a_slot_changes_that_slot()
    {
        StoryletDef storylet = Pair(
            "STORYLET_PAIR",
            Avoids,
            null,
            Option("blame", Toward("trait_g", 0), Loyalty(-2, "target"), new StoryletEffect(Trait: new TraitChange("trait_j", 1, false, "target")), new StoryletEffect(Morale: 3, MoraleOn: "subject")),
            Option("keep", Toward("trait_g", 0)));
        var h = new Harness(Pack(4, storylet));
        h.Mission(Toward("trait_g", 2, "CREW_02", "CREW_03"));

        List<IEvent> events = h.Send(new AnswerStorylet("2:STORYLET_PAIR", "blame"));

        Assert.Equal(
            [new TraitChanged("trait_g", "CREW_02", "CREW_03", 2, 0, TraitCauses.Answer, null), new TraitChanged("trait_j", "CREW_03", null, 0, 1, TraitCauses.Answer, null)],
            events.OfType<TraitChanged>());
        Assert.Equal(["CREW_03"], events.OfType<LoyaltyChanged>().Select(l => l.CrewId));
        Assert.Equal(["CREW_02"], events.OfType<MoraleChanged>().Select(m => m.CrewId));
        StoryletAnswered answered = events.OfType<StoryletAnswered>().Single();
        Assert.Equal(("2:STORYLET_PAIR", "subject=CREW_02, target=CREW_03", "blame"), (answered.OfferId, Show(answered.Cast), answered.OptionId));
    }

    // The fixture's storylets and vignettes run on the module as migrated: STORYLET_03 is cast with its holder and target.
    [Fact]
    public void The_fixtures_pair_storylet_is_cast_from_its_trait()
    {
        var h = new Harness(TestContent.Pack with { Vignettes = [] });

        h.Mission(Toward("trait_g", 3, "CREW_05", "CREW_01"));

        Assert.Equal("subject=CREW_05, target=CREW_01", h.CastOf("STORYLET_03"));
    }

    // 1e part four, Task 5 (design 4.5 and 4.6): salience and cooldowns. Keep leaves everything as it was, so a storylet answered
    // with it still holds; only a cooldown or salience keeps it from arriving again at once.
    private static readonly StoryletOption Keep = Option("keep");

    private static StoryletDef Held(string id, int stakes, string subject, Cooldown? cooldown = null)
    {
        return Crew(id, stakes, subject, Keep, Option("clear", Set("trait_b", 0))) with { Cooldown = cooldown ?? Cooldown.None };
    }

    // Each storylet offered, as "ID" or "ID:CREW".
    private static List<string> Offered(IEnumerable<IEvent> events)
    {
        return events.OfType<StoryletOffered>().Select(o => o.Cast.Count == 0 ? o.StoryletId : $"{o.StoryletId}:{string.Join(":", o.Cast.Values)}").ToList();
    }

    // Each cycle's offers, the cycles split by " | ".
    private static string Cycles(params List<string>[] cycles)
    {
        return string.Join(" | ", cycles.Select(c => string.Join(", ", c)));
    }

    // Answers every waiting storylet with keep, then opens the next itinerary: what it offers.
    private static List<string> NextCycle(Harness h)
    {
        foreach (string offer in h.Waiting.ToList())
        {
            h.Send(new AnswerStorylet(offer, "keep"));
        }

        return Offered(h.To(CyclePhase.Itinerary));
    }

    // Stakes 3 against 1, one clause each: 31 against 11, with one storylet a cycle.
    [Fact]
    public void Higher_stakes_wins()
    {
        var h = new Harness(Pack(1, Held("STORYLET_LOW", 1, "subject.trait.trait_b >= 1"), Held("STORYLET_HIGH", 3, "subject.trait.trait_b >= 1")));

        List<IEvent> events = h.Mission(Raise("trait_b", 1, "CREW_02"));

        StoryletOffered offered = Assert.Single(events.OfType<StoryletOffered>());
        Assert.Equal(("STORYLET_HIGH", 31), (offered.StoryletId, offered.Salience));
    }

    // Specificity is the top-level && clauses across the cast and requires: 2 against 1 at stakes 2 is 22 against 21, though the
    // vaguer one is first in the pack.
    [Fact]
    public void More_clauses_wins_at_equal_stakes()
    {
        StoryletDef vague = Held("STORYLET_VAGUE", 2, "subject.trait.trait_b >= 1");
        StoryletDef exact = Crew("STORYLET_EXACT", 2, "subject.trait.trait_b >= 1 && subject.fit", Keep, Option("clear", Set("trait_b", 0)))
            with { Requires = new Expr("subject.morale > 0 && subject.loyalty > 0") };
        var h = new Harness(Pack(1, vague, exact));

        List<IEvent> events = h.Mission(Raise("trait_b", 1, "CREW_02"));

        StoryletOffered offered = Assert.Single(events.OfType<StoryletOffered>());
        Assert.Equal(("STORYLET_EXACT", 24), (offered.StoryletId, offered.Salience));
    }

    // Freshness: salience.repeat (3) for each arrival in the last salience.window cycles. The more specific one (22) arrives, then
    // loses to the other (21) at 19, then wins again at 19 against 18.
    [Fact]
    public void A_storylet_that_just_fired_loses_ground()
    {
        StoryletDef first = Held("STORYLET_FIRST", 2, "subject.trait.trait_b >= 1 && subject.fit");
        StoryletDef second = Held("STORYLET_SECOND", 2, "subject.trait.trait_b >= 1");
        var h = new Harness(Pack(1, second, first));

        List<string> two = Offered(h.Mission(Raise("trait_b", 5, "CREW_02")));
        List<string> three = NextCycle(h);
        List<string> four = NextCycle(h);

        Assert.Equal("STORYLET_FIRST:CREW_02 | STORYLET_SECOND:CREW_02 | STORYLET_FIRST:CREW_02", Cycles(two, three, four));
        Assert.Equal([2, 4], h.Storylets.Fired.Single(f => f.Id == "STORYLET_FIRST").Cycles);
    }

    // Fires older than salience.window no longer count.
    [Fact]
    public void Freshness_reads_only_the_window()
    {
        StoryletDef first = Held("STORYLET_FIRST", 2, "subject.trait.trait_b >= 1 && subject.fit");
        StoryletDef second = Held("STORYLET_SECOND", 2, "subject.trait.trait_b >= 1");
        var h = new Harness(Pack(1, second, first) with { Salience = new SalienceTuning(10, 1, 3, 1) });

        List<string> two = Offered(h.Mission(Raise("trait_b", 5, "CREW_02")));
        List<string> three = NextCycle(h);
        List<string> four = NextCycle(h);

        // In cycle 4, first's arrival in cycle 2 is out of a window of 1: 22 against second's 21 - 3.
        Assert.Equal("STORYLET_FIRST:CREW_02 | STORYLET_SECOND:CREW_02 | STORYLET_FIRST:CREW_02", Cycles(two, three, four));
        Assert.Equal([4], h.Storylets.Fired.Single(f => f.Id == "STORYLET_FIRST").Cycles);
    }

    // Review focus 3: two storylets of equal salience are ordered by a draw from salience:<cycle>, so the same seed picks the same
    // one and some other seed the other. Ship storylets draw from no cast stream, so the salience stream is the only one.
    [Fact]
    public void Ties_are_drawn_from_the_salience_stream()
    {
        StoryletDef x = Ship("STORYLET_X", 2, "ship.trait.trait_a == 1", Keep, Option("clear", Set("trait_a", 0)));
        StoryletDef y = Ship("STORYLET_Y", 2, "ship.trait.trait_a >= 1", Keep, Option("clear", Set("trait_a", 0)));
        string Winner(ulong seed)
        {
            var h = new Harness(Pack(1, x, y), seed);
            List<IEvent> events = h.Mission(Raise("trait_a", 1));
            Assert.Contains("2:salience:2", h.State.RngStates.Keys);
            return Assert.Single(events.OfType<StoryletOffered>()).StoryletId;
        }

        Assert.Equal(Winner(1), Winner(1));
        Assert.Equal(["STORYLET_X", "STORYLET_Y"], Enumerable.Range(1, 20).Select(s => Winner((ulong)s)).Distinct().Order(StringComparer.Ordinal));
    }

    // Design 4.6: cooldown.cast holds back one binding only, inside the search, so the other holder has it next cycle. Arrived in
    // cycle c, a binding is held through cycle c + 3 with a cooldown of 4.
    [Fact]
    public void Cast_cooldown_holds_the_same_binding_only()
    {
        var h = new Harness(Pack(2, Held("STORYLET_HELD", 1, "subject.trait.trait_b >= 1", new Cooldown(4, 0))));

        List<string> two = Offered(h.Mission(Raise("trait_b", 5, "CREW_01"), Raise("trait_b", 5, "CREW_02")));
        List<string> three = NextCycle(h);
        List<string> four = NextCycle(h);
        List<string> five = NextCycle(h);
        List<string> six = NextCycle(h);

        string first = Assert.Single(two);
        string other = first.EndsWith("CREW_01", StringComparison.Ordinal) ? "STORYLET_HELD:CREW_02" : "STORYLET_HELD:CREW_01";
        Assert.Equal($"{other} |  |  | {first}", Cycles(three, four, five, six));
    }

    // cooldown.global holds every cast, and is read before casting: no cast stream is drawn while it holds.
    [Fact]
    public void Global_cooldown_holds_every_cast()
    {
        var h = new Harness(Pack(2, Held("STORYLET_HELD", 1, "subject.trait.trait_b >= 1", new Cooldown(0, 3))));

        List<string> two = Offered(h.Mission(Raise("trait_b", 5, "CREW_01"), Raise("trait_b", 5, "CREW_02")));
        List<string> three = NextCycle(h);
        Assert.DoesNotContain(h.State.RngStates.Keys, k => k.StartsWith("3:cast:", StringComparison.Ordinal));
        List<string> four = NextCycle(h);
        List<string> five = NextCycle(h);

        Assert.Single(two);
        Assert.Equal(" | ", Cycles(three, four));
        Assert.Single(five);
    }

    // The global cooldown is the storylet's, so a cast member's death does not lift it, as it drops that cast's own arrival.
    [Fact]
    public void A_death_does_not_lift_the_global_cooldown()
    {
        var h = new Harness(Pack(2, Held("STORYLET_HELD", 1, "subject.trait.trait_b >= 1", new Cooldown(0, 3))));
        h.Mission(Raise("trait_b", 5, "CREW_01"), Raise("trait_b", 5, "CREW_02"));
        string cast = h.Storylets.Waiting.Single().Cast["subject"];
        h.Send(new AnswerStorylet(h.Waiting.Single(), "keep"));

        h.To(CyclePhase.Resolution);
        h.End([], Died(cast));
        List<string> three = Offered(h.To(CyclePhase.Itinerary));

        Assert.Empty(three);
        Assert.DoesNotContain(h.Storylets.Arrivals, a => a.Id == "STORYLET_HELD");
    }

    // Storylets that cast and lose on the budget are not lost: they are cast again next cycle and arrive if they still hold.
    [Fact]
    public void Losers_on_the_budget_arrive_later()
    {
        StoryletDef high = Crew("STORYLET_HIGH", 3, "subject.trait.trait_j >= 1", Option("clear", Set("trait_j", 0)), Option("other", Set("trait_j", 0)));
        StoryletDef low = Held("STORYLET_LOW", 1, "subject.trait.trait_b >= 1");
        var h = new Harness(Pack(1, low, high));

        List<string> two = Offered(h.Mission(Raise("trait_j", 1, "CREW_03"), Raise("trait_b", 1, "CREW_02")));
        h.Send(new AnswerStorylet(h.Waiting.Single(), "clear"));
        List<string> three = Offered(h.To(CyclePhase.Itinerary));

        Assert.Equal("STORYLET_HIGH:CREW_03 | STORYLET_LOW:CREW_02", Cycles(two, three));
    }

    // Review focus 3 and Task 7: the one function the module runs lists every storylet and vignette with its fate, and for those
    // that cast, the score in parts. It changes nothing, so it can be read as a dry run. In cycle 2, high, again, and low arrive
    // (31, 21, 12) and fresh loses (11); in cycle 3 high waits, low is in its global cooldown, and again has fired once.
    [Fact]
    public void The_contest_gives_each_its_score_in_parts_and_its_fate()
    {
        StoryletDef high = Crew("STORYLET_HIGH", 3, "subject.trait.trait_j >= 1", Option("clear", Set("trait_j", 0)), Option("other", Set("trait_j", 0)));
        StoryletDef again = Held("STORYLET_AGAIN", 2, "subject.trait.trait_b >= 1");
        StoryletDef low = Held("STORYLET_LOW", 1, "subject.trait.trait_b >= 1 && subject.fit", new Cooldown(0, 5));
        StoryletDef fresh = Held("STORYLET_FRESH", 1, "subject.trait.trait_b >= 1");
        StoryletDef never = Held("STORYLET_NEVER", 1, "subject.trait.trait_c >= 1");
        var glance = new VignetteDef("VIGNETTE_GLANCE", [], new Expr("ship.trait.trait_a >= 0"), [new StoryletEffect(Morale: 1, MoraleOn: "crew")], [], null) { Cooldown = Cooldown.None };
        ContentPack pack = Pack(3, high, again, low, fresh, never) with { Vignettes = [glance] };
        var h = new Harness(pack);
        h.Mission(Raise("trait_j", 1, "CREW_03"), Raise("trait_b", 1, "CREW_02"));
        Assert.Equal(["2:STORYLET_HIGH", "2:STORYLET_AGAIN", "2:STORYLET_LOW"], h.Waiting);
        h.Send(new AnswerStorylet("2:STORYLET_AGAIN", "keep"));
        h.Send(new AnswerStorylet("2:STORYLET_LOW", "keep"));
        string before = JsonSerializer.Serialize(h.Storylets);

        Contest Run()
        {
            var source = new StateSource(h.Bus.View<IRosterView>(h.State), h.Bus.View<ITraitView>(h.State), h.Storylets, 3);
            return Salience.Contend(pack, new Casting(pack), h.Storylets, source, h.Bus.View<IRosterView>(h.State).Roster, 3, name => RngStream.For(1, 3, name));
        }

        Contest contest = Run();

        Assert.Equal(before, JsonSerializer.Serialize(h.Storylets));
        Assert.Equal(JsonSerializer.Serialize(contest), JsonSerializer.Serialize(Run()));
        static string Line(Contender c)
        {
            return $"{c.Id} {c.Fate}{(c.Score is { } s ? $" {s.Stakes}+{s.Specificity}-{s.Freshness}" : "")}{(c.ReturnsAt is { } at ? $" until {at}" : "")}";
        }

        Assert.Equal(
            ["STORYLET_AGAIN Arrived 20+1-3", "STORYLET_FRESH Arrived 10+1-0", "STORYLET_HIGH Waiting", "STORYLET_LOW Cooling until 7", "STORYLET_NEVER NoCast"],
            contest.Storylets.Select(Line));
        Assert.Equal(["subject=CREW_02", "subject=CREW_02"], contest.Storylets.Take(2).Select(c => Show(c.Cast!)));
        Assert.Equal(18, contest.Storylets[0].Score!.Total);
        Assert.Equal(["VIGNETTE_GLANCE Arrived 0+1-6"], contest.Vignettes.Select(Line));
    }

    // Review focus 3 and Task 7 (design 4.10): Dev Play's dry run. Between cycles it foresees the next itinerary; once that is open,
    // it gives the contest as it ran, even after an answer has cleared the trait it read. It advances no stream and changes nothing,
    // so a legacy that ran it plays on exactly as one that did not. STORYLET_LOW and STORYLET_FRESH tie, so the tie stream is drawn.
    [Fact]
    public void The_dry_run_advances_no_stream()
    {
        StoryletDef high = Crew("STORYLET_HIGH", 3, "subject.trait.trait_j >= 1", Option("clear", Set("trait_j", 0)), Option("other", Set("trait_j", 0)));
        ContentPack pack = Pack(2, high, Held("STORYLET_LOW", 1, "subject.trait.trait_b >= 1"), Held("STORYLET_FRESH", 1, "subject.trait.trait_b >= 1"));
        var watched = new Harness(pack);
        var plain = new Harness(pack);
        foreach (Harness h in new[] { watched, plain })
        {
            h.To(CyclePhase.Resolution);
            h.End([Raise("trait_j", 3, "CREW_03"), Raise("trait_b", 5, "CREW_02")]);
            h.To(CyclePhase.Aftermath);
        }

        string Snapshot()
        {
            return JsonSerializer.Serialize(new { watched.State.RngStates, watched.Storylets, watched.Traits, watched.Crew });
        }

        static string Events(IEnumerable<IEvent> events)
        {
            return string.Join("\n", events.Select(e => e.GetType().Name + JsonSerializer.Serialize(e, e.GetType())));
        }

        string before = Snapshot();
        Contest foreseen = watched.Bus.View<IStoryletDryRun>(watched.State).Contest([]);
        Assert.Equal(before, Snapshot());
        Assert.Equal((2, false), (foreseen.Cycle, foreseen.Open));

        List<IEvent> opened = watched.To(CyclePhase.Itinerary);
        Assert.Equal(Events(plain.To(CyclePhase.Itinerary)), Events(opened));
        Assert.Equal(Offered(opened), foreseen.Storylets.Where(c => c.Fate == ContestFate.Arrived).Select(c => $"{c.Id}:{string.Join(":", c.Cast!.Values)}"));
        Assert.Equal(3, foreseen.Storylets.Count(c => c.Score is not null));

        opened.AddRange(watched.Send(new AnswerStorylet("2:STORYLET_HIGH", "clear")));
        plain.Send(new AnswerStorylet("2:STORYLET_HIGH", "clear"));
        before = Snapshot();
        Contest open = watched.Bus.View<IStoryletDryRun>(watched.State).Contest(opened);
        Assert.Equal(before, Snapshot());
        Assert.Equal((2, true), (open.Cycle, open.Open));
        Assert.Equal(JsonSerializer.Serialize(foreseen.Storylets), JsonSerializer.Serialize(open.Storylets));

        foreach (string offer in watched.Waiting.ToList())
        {
            watched.Send(new AnswerStorylet(offer, "keep"));
            plain.Send(new AnswerStorylet(offer, "keep"));
        }

        Assert.Equal(Events(plain.To(CyclePhase.Itinerary)), Events(watched.To(CyclePhase.Itinerary)));
        Assert.Equal(JsonSerializer.Serialize(plain.State.RngStates), JsonSerializer.Serialize(watched.State.RngStates));
    }

    [Fact]
    public void A_storylet_on_the_budget_loses_with_its_cast_and_score()
    {
        StoryletDef high = Crew("STORYLET_HIGH", 3, "subject.trait.trait_j >= 1", Option("clear", Set("trait_j", 0)), Option("other", Set("trait_j", 0)));
        StoryletDef low = Held("STORYLET_LOW", 1, "subject.trait.trait_b >= 1");
        ContentPack pack = Pack(1, low, high);
        var h = new Harness(pack);
        h.To(CyclePhase.Resolution);
        h.End([Raise("trait_j", 1, "CREW_03"), Raise("trait_b", 1, "CREW_02")]);
        h.To(CyclePhase.Aftermath);

        var source = new StateSource(h.Bus.View<IRosterView>(h.State), h.Bus.View<ITraitView>(h.State), h.Storylets, 2);
        Contest contest = Salience.Contend(pack, new Casting(pack), h.Storylets, source, h.Bus.View<IRosterView>(h.State).Roster, 2, name => RngStream.For(1, 2, name));

        Assert.Equal(["STORYLET_HIGH Arrived 31", "STORYLET_LOW LostOnBudget 11"], contest.Storylets.Select(c => $"{c.Id} {c.Fate} {c.Score!.Total}"));
        Assert.Equal("subject=CREW_02", Show(contest.Storylets[1].Cast!));
    }

    // What the casting search reads in Out_of_tries_is_told_apart_from_no_cast: trait_b by crew member, and every one fit.
    private sealed class Source(Dictionary<string, int> traitB) : IStateSource
    {
        public int Cycle => 2;

        public int Morale(string crewId)
        {
            return 50;
        }

        public int Loyalty(string crewId)
        {
            return 50;
        }

        public int Skill(string crewId, string skill)
        {
            return 0;
        }

        public bool IsFit(string crewId)
        {
            return true;
        }

        public bool IsAboard(string crewId)
        {
            return true;
        }

        public int Trait(string trait, string? crewId, string? targetId = null)
        {
            return trait == "trait_b" && crewId is not null && targetId is null ? traitB.GetValueOrDefault(crewId) : 0;
        }

        public int History(string crewId, string kind, int cycles)
        {
            return 0;
        }
    }
}

using Sim.Chronicle;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Crew;
using Sim.Cycle;
using Sim.Missions;
using Sim.Narrative;
using Sim.Traits;

namespace Sim.Tests;

// Slice 1e part five: the chronicle module, its narrator, and its view.
public class ChronicleModuleTests
{
    // 13 MONTH_B 105, the fixture calendar's start.
    private const int StartDay = 37482;

    private static EventBus NewBus(ContentPack pack)
    {
        var bus = new EventBus();
        CycleModule.Register(bus, pack);
        CrewModule.Register(bus, pack);
        ChronicleModule.Register(bus, pack, new Narrator(pack));
        return bus;
    }

    private static (LegacyState State, IReadOnlyList<PublishedEvent> Published) Found(ContentPack pack, ulong seed = 1)
    {
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = seed };
        IReadOnlyList<PublishedEvent> published = NewBus(pack).Send(state, new FoundLegacy());
        return (state, published);
    }

    private static Situation Founding(int crew)
    {
        return new Situation(
            NarrativeFacts.Chronicle.Founding,
            new Dictionary<string, FactValue> { ["crew"] = FactValue.Of(crew) },
            new Dictionary<string, Person>(),
            new Dictionary<string, string> { ["crew"] = crew.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private static LineDef Line(string id, string text, params CriterionDef[] when)
    {
        return new LineDef(id, NarrativeFacts.Chronicle.Founding, when, text);
    }

    [Fact]
    public void The_narrator_expands_the_line_that_holds()
    {
        ContentPack pack = TestContent.Pack with { Lines = [Line("LINE_A", "A crew of #crew#."), Line("LINE_B", "A crew of eight.", new CriterionDef("crew", ["8"]))] };

        var narrator = new Narrator(pack);

        Assert.Equal("A crew of eight.", narrator.Line(Founding(8), "key"));
        Assert.Equal("A crew of 3.", narrator.Line(Founding(3), "key"));
    }

    [Fact]
    public void The_narrator_gives_null_when_no_line_holds()
    {
        ContentPack pack = TestContent.Pack with { Lines = [Line("LINE_B", "A crew of eight.", new CriterionDef("crew", ["8"]))] };

        Assert.Null(new Narrator(pack).Line(Founding(3), "key"));
    }

    // Ties break by the key alone: no line is passed over for having been used, so asking again gives the same words.
    [Fact]
    public void The_narrator_gives_the_same_line_for_the_same_key()
    {
        ContentPack pack = TestContent.Pack with { Lines = Enumerable.Range(1, 6).Select(i => Line($"LINE_{i}", $"WORDS_{i}.")).ToList() };
        var narrator = new Narrator(pack);

        List<string?> first = Enumerable.Range(1, 20).Select(i => narrator.Line(Founding(8), $"key|{i}")).ToList();
        List<string?> again = Enumerable.Range(1, 20).Select(i => narrator.Line(Founding(8), $"key|{i}")).ToList();

        Assert.Equal(first, again);
        Assert.True(first.Distinct().Count() > 1, "different keys should spread over the tied lines");
    }

    [Fact]
    public void The_founding_writes_one_entry_with_its_day_and_frozen_words()
    {
        (LegacyState state, IReadOnlyList<PublishedEvent> published) = Found(TestContent.Pack);
        long founded = published.Single(p => p.Event is LegacyFounded).Sequence;

        ChronicleEntry entry = Assert.Single(state.Module<ChronicleState>(ChronicleModule.Name).Entries);

        Assert.Equal(("chr_0001", StartDay, 1, ChronicleKinds.Founding, 5, "CHRONICLE_FOUNDING 8."), (entry.Id, entry.Day, entry.Cycle, entry.Kind, entry.Weight, entry.Text));
        Assert.Empty(entry.Subjects);
        Assert.Equal([founded], entry.SourceSeq);
    }

    [Fact]
    public void With_no_line_the_founding_falls_back_to_fixed_words()
    {
        (LegacyState state, _) = Found(TestContent.Pack with { Lines = [] });

        Assert.Equal(ChronicleModule.FoundingFallback, Assert.Single(state.Module<ChronicleState>(ChronicleModule.Name).Entries).Text);
    }

    [Fact]
    public void A_weight_of_0_writes_no_entry_and_spends_no_id()
    {
        var weights = new Dictionary<string, int>(ChronicleTuning.Default.Weights) { [ChronicleTuning.Founding] = 0 };

        (LegacyState state, _) = Found(TestContent.Pack with { Chronicle = new ChronicleTuning(weights, 3) });

        ChronicleState chronicle = state.Module<ChronicleState>(ChronicleModule.Name);
        Assert.Empty(chronicle.Entries);
        Assert.Equal(0, chronicle.Counter);
    }

    // The window is days, today included: the day `days - 1` back counts and the day before it does not.
    [Fact]
    public void Count_holds_today_and_the_days_before_it_within_the_window()
    {
        EventBus bus = NewBus(TestContent.Pack);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1, Day = 1000 };
        state.Module<ChronicleState>(ChronicleModule.Name).Entries.AddRange(
        [
            Entry("chr_0001", 1000, ChronicleKinds.Death),
            Entry("chr_0002", 881, ChronicleKinds.Death),
            Entry("chr_0003", 880, ChronicleKinds.Death),
            Entry("chr_0004", 1000, ChronicleKinds.Mission),
        ]);

        IChronicleView view = bus.View<IChronicleView>(state);

        Assert.Equal(2, view.Count(ChronicleKinds.Death, 120));
        Assert.Equal(3, view.Count(ChronicleKinds.Death, 121));
        Assert.Equal(1, view.Count(ChronicleKinds.Death, 1));
        Assert.Equal(1, view.Count(ChronicleKinds.Mission, 1));
        Assert.Equal(0, view.Count(ChronicleKinds.Call, 1000));
        Assert.Equal(["chr_0001", "chr_0002", "chr_0003", "chr_0004"], view.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Count_for_a_crew_member_reads_only_the_entries_they_are_a_subject_of()
    {
        EventBus bus = NewBus(TestContent.Pack);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1, Day = 1000 };
        state.Module<ChronicleState>(ChronicleModule.Name).Entries.AddRange(
        [
            Entry("chr_0001", 990, ChronicleKinds.Call, "CREW_01"),
            Entry("chr_0002", 995, ChronicleKinds.Call, "CREW_02"),
            Entry("chr_0003", 999, ChronicleKinds.Storylet, "CREW_02", "CREW_01"),
            Entry("chr_0004", 900, ChronicleKinds.Call, "CREW_01"),
        ]);

        IChronicleView view = bus.View<IChronicleView>(state);

        Assert.Equal(1, view.Count("CREW_01", ChronicleKinds.Call, 60));
        Assert.Equal(2, view.Count("CREW_01", ChronicleKinds.Call, 101));
        Assert.Equal(1, view.Count("CREW_01", ChronicleKinds.Storylet, 60));
        Assert.Equal(0, view.Count("CREW_03", ChronicleKinds.Call, 1000));
        Assert.Equal(3, view.Count(ChronicleKinds.Call, 101));
    }

    // A view reads without writing: an unfounded legacy has no chronicle, and reading one leaves it so.
    [Fact]
    public void Reading_the_view_creates_no_state()
    {
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };

        Assert.Empty(NewBus(TestContent.Pack).View<IChronicleView>(state).Entries);
        Assert.False(state.Modules.ContainsKey(ChronicleModule.Name));
    }

    // Task 3: deaths, calls, and missions. A legacy with every module that publishes what the chronicle hears, its first mission
    // assigned to three with CREW_01 leading, and the cycle at resolution.
    private sealed class Legacy
    {
        public Legacy(ulong seed, ContentPack? pack = null)
        {
            pack ??= TestContent.Pack;
            Bus = new EventBus();
            CycleModule.Register(Bus, pack);
            CrewModule.Register(Bus, pack);
            MissionsModule.Register(Bus, pack);
            TraitsModule.Register(Bus, pack);
            ChronicleModule.Register(Bus, pack, new Narrator(pack));
            State = new LegacyState { LegacyId = Guid.Empty, Seed = seed };
            Send(new FoundLegacy());
            GoTo(CyclePhase.Briefing);
            SiteState site = State.Module<MissionsState>(MissionsModule.Name).Site!;
            Send(new AssignTeam(site.Objectives[0].Id, ["CREW_01", "CREW_02", "CREW_03"], "CREW_01", [], new Parameters()));
            GoTo(CyclePhase.Resolution);
        }

        public EventBus Bus { get; }

        public LegacyState State { get; }

        public List<PublishedEvent> Events { get; } = new();

        public MissionInFlight Mission => State.Module<MissionsState>(MissionsModule.Name).Site!.Mission!;

        public ChronicleState Chronicle => State.Module<ChronicleState>(ChronicleModule.Name);

        public IEnumerable<T> Published<T>() => Events.Select(e => e.Event).OfType<T>();

        public void Send(ICommand command) => Events.AddRange(Bus.Send(State, command));

        // Answers every call until the mission ends: against the lead, or with them.
        public void Finish(bool overrule)
        {
            while (Mission.State.Pending is { } call)
            {
                CallAnswer answer = !overrule ? call.LeadPrefers : call.LeadPrefers == CallAnswer.Bold ? CallAnswer.Safe : CallAnswer.Bold;
                Send(new AnswerCall(Mission.Id, call.CallId, answer));
            }
        }

        public long Seq<T>(Func<T, bool>? which = null)
            where T : IEvent
        {
            return Events.First(e => e.Event is T t && (which is null || which(t))).Sequence;
        }

        private void GoTo(CyclePhase phase)
        {
            while (State.Phase != phase)
            {
                Send(new AdvancePhase());
            }
        }
    }

    private static Legacy Played(bool overrule, Func<Legacy, bool> wanted, ContentPack? pack = null)
    {
        for (ulong seed = 1; seed < 2000; seed++)
        {
            var legacy = new Legacy(seed, pack);
            legacy.Finish(overrule);
            if (wanted(legacy))
            {
                return legacy;
            }
        }

        throw new InvalidOperationException("No seed in range plays the mission wanted.");
    }

    private static ContentPack WithDecisionWeight(int weight)
    {
        return TestContent.Pack with
        {
            Encounters = TestContent.Pack.Encounters.Select(e => e.Decision is { } d ? e with { Decision = d with { ChronicleWeight = weight } } : e).ToList(),
        };
    }

    // The crew module publishes the deaths after MissionEnded; the mission's entry still follows them (design 4.3).
    [Fact]
    public void A_mission_with_an_overruled_call_and_a_death_writes_the_call_the_death_and_the_mission_in_order()
    {
        Legacy legacy = Played(true, l => l.Published<CrewDied>().Count() == 1 && l.Published<CallAnswered>().Any(c => c.Standing == "overruled"));
        CrewDied died = legacy.Published<CrewDied>().Single();
        MissionEnded ended = legacy.Published<MissionEnded>().Single();
        List<CallAnswered> calls = legacy.Published<CallAnswered>().ToList();

        List<ChronicleEntry> entries = legacy.Chronicle.Entries.Skip(1).ToList();

        Assert.Equal([.. calls.Select(_ => ChronicleKinds.Call), ChronicleKinds.Death, ChronicleKinds.Mission], entries.Select(e => e.Kind));
        CallAnswered overruled = calls.First(c => c.Standing == "overruled");
        ChronicleEntry call = entries.Single(e => e.SourceSeq[0] == legacy.Seq<CallAnswered>(c => c == overruled));
        Assert.Equal((3, $"CHRONICLE_CALL_OVERRULED CREW_01 {overruled.EncounterId}."), (call.Weight, call.Text));
        Assert.Equal([new ChronicleSubject("CREW_01", "CREW_01")], call.Subjects);

        ChronicleEntry death = entries[^2];
        Assert.Equal((5, $"CHRONICLE_DEATH {died.CrewId} {died.EncounterId} CREW_01 1 cycle."), (death.Weight, death.Text));
        Assert.Equal([new ChronicleSubject(died.CrewId, died.CrewId)], death.Subjects);
        Assert.Equal([legacy.Seq<CrewDied>()], death.SourceSeq);

        ChronicleEntry mission = entries[^1];
        bool won = ended.Result.Outcome is MissionOutcome.FullSuccess or MissionOutcome.PartialSuccess;
        string template = legacy.Published<MissionStarted>().Single().TemplateId;
        Assert.Equal((won ? 1 : 2, $"{(won ? "CHRONICLE_MISSION" : "CHRONICLE_MISSION_FAILED")} CREW_01 {template} 3 1."), (mission.Weight, mission.Text));
        Assert.Equal([new ChronicleSubject("CREW_01", "CREW_01")], mission.Subjects);
        Assert.Equal([legacy.Seq<MissionEnded>(), legacy.Seq<CrewDied>()], mission.SourceSeq);
        Assert.Empty(legacy.Chronicle.Missions);
    }

    [Fact]
    public void A_backed_call_weighs_2_and_a_mission_with_no_death_is_written_on_its_end()
    {
        Legacy legacy = Played(false, l => l.Published<CallAnswered>().Any(c => c.Standing == "backed") && !l.Published<CrewDied>().Any());

        ChronicleEntry call = legacy.Chronicle.Entries.Single(e => e.SourceSeq[0] == legacy.Seq<CallAnswered>(c => c.Standing == "backed"));

        Assert.Equal((ChronicleKinds.Call, 2), (call.Kind, call.Weight));
        Assert.StartsWith("CHRONICLE_CALL CREW_01 ", call.Text, StringComparison.Ordinal);
        ChronicleEntry mission = legacy.Chronicle.Entries[^1];
        Assert.Equal(ChronicleKinds.Mission, mission.Kind);
        Assert.Equal([legacy.Seq<MissionEnded>()], mission.SourceSeq);
        Assert.EndsWith(" 3 0.", mission.Text, StringComparison.Ordinal);
        Assert.Empty(legacy.Chronicle.Missions);
    }

    [Fact]
    public void A_decision_with_a_chronicle_weight_of_0_writes_no_call_entry()
    {
        Legacy legacy = Played(true, l => l.Published<CallAnswered>().Any(c => c.DecisionId != DecisionIds.Abort), WithDecisionWeight(0));

        List<long> answered = legacy.Events.Where(e => e.Event is CallAnswered c && c.DecisionId != DecisionIds.Abort).Select(e => e.Sequence).ToList();

        Assert.DoesNotContain(legacy.Chronicle.Entries, e => answered.Contains(e.SourceSeq[0]));
        Assert.Equal(ChronicleKinds.Mission, legacy.Chronicle.Entries[^1].Kind);
    }

    // A decision's own weight wins over the tuning file's, whichever way the call went.
    [Fact]
    public void A_decision_with_a_chronicle_weight_writes_its_call_at_that_weight()
    {
        Legacy legacy = Played(false, l => l.Published<CallAnswered>().Any(c => c.DecisionId != DecisionIds.Abort), WithDecisionWeight(5));

        long answered = legacy.Seq<CallAnswered>(c => c.DecisionId != DecisionIds.Abort);

        Assert.Equal(5, legacy.Chronicle.Entries.Single(e => e.SourceSeq[0] == answered).Weight);
    }

    // What a mission's entries name is kept until its own entry is written, and survives a save between its calls.
    [Fact]
    public void A_mission_waiting_on_a_call_is_kept_with_its_objective_site_team_and_lead()
    {
        for (ulong seed = 1; seed < 2000; seed++)
        {
            var legacy = new Legacy(seed);
            if (legacy.Mission.State.Pending is null)
            {
                continue;
            }

            OpenMission open = Assert.Single(legacy.Chronicle.Missions);
            MissionStarted started = legacy.Published<MissionStarted>().Single();
            Assert.Equal(
                (started.MissionId, started.TemplateId, legacy.Published<SiteArrived>().Last().AssignmentId, "CREW_01", (string?)null),
                (open.Id, open.ObjectiveId, open.SiteId, open.LeadId, open.Ended));
            Assert.Equal(["CREW_01", "CREW_02", "CREW_03"], open.Team);
            string json = System.Text.Json.JsonSerializer.Serialize(legacy.Chronicle, SimJson.Options);
            Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<ChronicleState>(json, SimJson.Options), SimJson.Options));
            return;
        }

        throw new InvalidOperationException("No seed in range raises a call.");
    }

    private static ChronicleEntry Entry(string id, int day, string kind, params string[] subjects)
    {
        return new ChronicleEntry(id, day, 1, kind, 3, subjects.Select(s => new ChronicleSubject(s, s)).ToList(), "TEXT.", [1]);
    }
}

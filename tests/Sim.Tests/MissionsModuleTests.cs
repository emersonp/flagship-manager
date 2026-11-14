using System.Text.Json;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Cycle;
using Sim.Missions;

namespace Sim.Tests;

public class MissionsModuleTests
{
    private static readonly EventTypeRegistry Registry = EventTypeRegistry.FromAssemblies(typeof(LegacyState).Assembly, typeof(CrewModule).Assembly, typeof(MissionsModule).Assembly);

    private static EventBus NewBus()
    {
        var bus = new EventBus();
        CycleModule.Register(bus);
        CrewModule.Register(bus, TestContent.Pack);
        MissionsModule.Register(bus, TestContent.Pack);
        return bus;
    }

    private sealed class Harness
    {
        public Harness(ulong seed = 1)
        {
            Bus = NewBus();
            State = new LegacyState { LegacyId = Guid.Empty, Seed = seed };
            Events.AddRange(Bus.Send(State, new FoundLegacy()));
        }

        public EventBus Bus { get; }

        public LegacyState State { get; }

        public List<PublishedEvent> Events { get; } = new();

        public SiteState Site => State.Module<MissionsState>("missions").Site!;

        public MissionInFlight? Mission => Site.Mission;

        public IReadOnlyList<PublishedEvent> Send(ICommand command)
        {
            IReadOnlyList<PublishedEvent> sent = Bus.Send(State, command);
            Events.AddRange(sent);
            return sent;
        }

        public void GoTo(CyclePhase phase)
        {
            while (State.Phase != phase)
            {
                Send(new AdvancePhase());
            }
        }

        public string FirstObjectiveId => Site.Objectives[0].Id;

        public AssignTeam Assign(string[] crew, string[]? gear = null, string? objectiveId = null)
        {
            return new AssignTeam(objectiveId ?? FirstObjectiveId, crew, crew[0], gear ?? [], new Parameters());
        }

        public string Snapshot()
        {
            return JsonSerializer.Serialize(new { State.Cycle, State.Phase, State.Week, State.NextSequence, State.RngStates, State.Modules }, SimJson.Options);
        }

        public CommandRejectedException Refused(ICommand command)
        {
            string before = Snapshot();
            var refusal = Assert.Throws<CommandRejectedException>(() => Bus.Send(State, command));
            Assert.Equal(before, Snapshot());
            return refusal;
        }

        public ObjectiveDef Template(string objectiveId)
        {
            return TestContent.Pack.Objectives.Single(o => o.Id == Site.Objectives.Single(s => s.Id == objectiveId).TemplateId);
        }

        // Answers every call the lead's way until the mission ends.
        public void Finish()
        {
            while (Mission!.State.Pending is { } call)
            {
                Send(new AnswerCall(Mission.Id, call.CallId, call.LeadPrefers));
            }
        }
    }

    private static readonly string[] Pair = ["CREW_01", "CREW_02"];

    private static Harness ToBriefing(ulong seed = 1)
    {
        var h = new Harness(seed);
        h.GoTo(CyclePhase.Briefing);
        return h;
    }

    // A legacy whose first mission stops on a call.
    private static Harness WaitingOnCall(bool withoutWithdraw = false)
    {
        for (ulong seed = 1; seed < 500; seed++)
        {
            Harness h = ToBriefing(seed);
            h.Send(h.Assign(Pair));
            h.GoTo(CyclePhase.Resolution);
            if (h.Mission!.State.Pending is { } pending && (!withoutWithdraw || !pending.Options.Contains(CallAnswer.Withdraw)))
            {
                return h;
            }
        }

        throw new InvalidOperationException("No seed in range raises a call.");
    }

    private static LegacyState Restore(Harness original)
    {
        var restored = new LegacyState
        {
            LegacyId = original.State.LegacyId,
            Seed = original.State.Seed,
            Cycle = original.State.Cycle,
            Phase = original.State.Phase,
            Week = original.State.Week,
            NextSequence = original.State.NextSequence,
        };
        foreach ((string key, ulong value) in original.State.RngStates)
        {
            restored.RngStates[key] = value;
        }

        foreach ((string name, object value) in original.State.Modules)
        {
            Type type = original.Bus.ModuleTypes[name];
            restored.Modules[name] = JsonSerializer.Deserialize(JsonSerializer.Serialize(value, type, SimJson.Options), type, SimJson.Options)!;
        }

        return restored;
    }

    private static List<string> Types(IEnumerable<PublishedEvent> events)
    {
        return events.Select(e => Registry.ToEnvelope(e).Type).ToList();
    }

    [Fact]
    public void A_site_offers_one_objective_more_often_than_two_and_never_repeats_a_template()
    {
        var h = new Harness(7);
        while (h.State.Cycle < 300)
        {
            h.Send(new AdvancePhase());
        }

        List<SiteArrived> sites = h.Events.Select(e => e.Event).OfType<SiteArrived>().ToList();
        int ones = sites.Count(s => s.Objectives.Count == 1);
        int twos = sites.Count(s => s.Objectives.Count == 2);

        Assert.Equal(300, sites.Count);
        Assert.True(ones > twos, $"{ones} one-objective sites, {twos} two-objective sites.");
        Assert.True(twos > 0);
        Assert.All(sites, s => Assert.Equal(s.Objectives.Count, s.Objectives.Select(o => o.TemplateId).Distinct().Count()));
        Assert.All(sites, s => Assert.Equal(TestContent.Pack.Assignments[0].Id, s.AssignmentId));
        Assert.Equal(new[] { "o_5_1" }, sites[4].Objectives.Select(o => o.Id).Take(1));
    }

    [Fact]
    public void Founding_the_legacy_draws_the_first_site()
    {
        var h = new Harness();

        Assert.Equal(1, h.Site.Cycle);
        Assert.Contains("SiteArrived", Types(h.Events));
    }

    [Fact]
    public void A_team_is_refused_outside_the_briefing()
    {
        var h = new Harness();

        Assert.Contains("briefing", h.Refused(h.Assign(Pair)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_team_is_refused_for_an_objective_not_at_the_site()
    {
        Harness h = ToBriefing();

        Assert.Contains("o_9_9", h.Refused(h.Assign(Pair, objectiveId: "o_9_9")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_team_too_small_is_refused()
    {
        Harness h = ToBriefing();

        Assert.Contains("team of 1", h.Refused(h.Assign(["CREW_01"])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_team_too_large_is_refused()
    {
        Harness h = ToBriefing();
        int size = h.Template(h.FirstObjectiveId).TeamSizeMax + 1;
        string[] crew = Enumerable.Range(1, size).Select(n => $"CREW_{n:00}").ToArray();

        Assert.Contains($"team of {size}", h.Refused(h.Assign(crew)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crew_member_listed_twice_is_refused()
    {
        Harness h = ToBriefing();

        Assert.Contains("twice", h.Refused(h.Assign(["CREW_01", "CREW_01"])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crew_member_not_on_the_roster_is_refused()
    {
        Harness h = ToBriefing();

        Assert.Contains("CREW_99", h.Refused(h.Assign(["CREW_01", "CREW_99"])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_crew_member_who_is_not_fit_is_refused()
    {
        Harness h = ToBriefing();
        h.State.Module<CrewState>("crew").Members.Single(m => m.Id == "CREW_02").Status = CrewStatus.Recovering;

        Assert.Contains("CREW_02", h.Refused(h.Assign(Pair)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lead_outside_the_team_is_refused()
    {
        Harness h = ToBriefing();

        string message = h.Refused(new AssignTeam(h.FirstObjectiveId, Pair, "CREW_03", [], new Parameters())).Message;

        Assert.Contains("lead", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_gear_is_refused()
    {
        Harness h = ToBriefing();

        Assert.Contains("GEAR_99", h.Refused(h.Assign(Pair, ["GEAR_99"])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gear_listed_twice_is_refused()
    {
        Harness h = ToBriefing();

        Assert.Contains("twice", h.Refused(h.Assign(Pair, ["GEAR_01", "GEAR_01"])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loadout_over_the_limit_is_refused()
    {
        Harness h = ToBriefing();
        int limit = TestContent.Pack.Assignments[0].LoadoutLimit;
        string[] gear = TestContent.Pack.Gear.Take(limit + 1).Select(g => g.Id).ToArray();

        Assert.Contains($"{limit}", h.Refused(h.Assign(Pair, gear)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_team_is_accepted_once_and_a_second_is_refused()
    {
        Harness h = ToBriefing();

        IReadOnlyList<PublishedEvent> events = h.Send(h.Assign(Pair, ["GEAR_01"]));

        TeamAssigned assigned = Assert.IsType<TeamAssigned>(Assert.Single(events).Event);
        Assert.Equal(h.FirstObjectiveId, assigned.ObjectiveId);
        Assert.Equal(Pair, assigned.CrewIds);
        Assert.Equal(["GEAR_01"], h.Site.Briefing!.GearIds);
        Assert.Contains("already", h.Refused(h.Assign(["CREW_03", "CREW_04"])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sending_nobody_rolls_every_objective_with_a_difficulty()
    {
        var h = new Harness(3);
        while (h.State.Cycle < 40)
        {
            h.Send(new AdvancePhase());
        }

        List<ObjectiveUnattended> rolls = h.Events.Select(e => e.Event).OfType<ObjectiveUnattended>().ToList();
        List<SiteArrived> sites = h.Events.Select(e => e.Event).OfType<SiteArrived>().Take(39).ToList();
        int expected = sites.Sum(s => s.Objectives.Count(o => TestContent.Pack.Objectives.Single(d => d.Id == o.TemplateId).UnattendedDifficulty is not null));

        Assert.Equal(expected, rolls.Count);
        Assert.NotEmpty(rolls);
        Assert.All(rolls, r =>
        {
            int difficulty = TestContent.Pack.Objectives.Single(d => d.Id == r.TemplateId).UnattendedDifficulty!.Value;
            Assert.Equal(difficulty + TestContent.Tuning.CheckTarget, r.Target);
            Assert.Equal(r.Roll >= r.Target, r.Succeeded);
        });
        Assert.Contains(rolls, r => r.Succeeded);
        Assert.Contains(rolls, r => !r.Succeeded);
    }

    [Fact]
    public void An_objective_with_a_team_is_not_rolled_unattended()
    {
        Harness h = ToBriefing();
        string attended = h.FirstObjectiveId;
        h.Send(h.Assign(Pair));
        h.GoTo(CyclePhase.Resolution);
        h.Finish();
        int before = h.Events.Count;

        h.GoTo(CyclePhase.Advance);

        Assert.DoesNotContain(h.Events.Skip(before).Select(e => e.Event).OfType<ObjectiveUnattended>(), r => r.ObjectiveId == attended);
        Assert.Equal(h.Site.Objectives.Count - 1, h.Events.Skip(before).Select(e => e.Event).OfType<ObjectiveUnattended>().Count());
    }

    [Fact]
    public void A_waiting_call_blocks_the_cycle_until_the_right_answer_runs_the_mission_on()
    {
        Harness h = WaitingOnCall();
        PendingCall call = h.Mission!.State.Pending!;
        string missionId = h.Mission.Id;

        Assert.Equal("A call is waiting for an answer.", h.Refused(new AdvancePhase()).Message);
        Assert.Contains("m_9_9", h.Refused(new AnswerCall("m_9_9", call.CallId, call.LeadPrefers)).Message, StringComparison.Ordinal);
        Assert.Contains("CALL_X", h.Refused(new AnswerCall(missionId, "CALL_X", call.LeadPrefers)).Message, StringComparison.Ordinal);
        int before = h.Events.Count;
        h.Finish();

        List<IEvent> after = h.Events.Skip(before).Select(e => e.Event).ToList();
        Assert.Single(after.OfType<MissionEnded>());
        Assert.IsType<MissionEnded>(after.Where(e => e is MissionEnded or MissionBeat).Last());
        h.Send(new AdvancePhase());
        Assert.Equal(CyclePhase.Aftermath, h.State.Phase);
    }

    [Fact]
    public void An_answer_that_is_not_an_option_is_refused_with_the_resolvers_message()
    {
        Harness h = WaitingOnCall(withoutWithdraw: true);
        PendingCall call = h.Mission!.State.Pending!;

        string message = h.Refused(new AnswerCall(h.Mission.Id, call.CallId, CallAnswer.Withdraw)).Message;

        Assert.Equal($"'Withdraw' is not an option for {call.DecisionId}.", message);
    }

    [Fact]
    public void Every_listed_option_of_a_pending_call_is_accepted()
    {
        Harness h = WaitingOnCall();
        PendingCall call = h.Mission!.State.Pending!;

        foreach (CallAnswer option in call.Options)
        {
            LegacyState copy = Restore(h);
            h.Bus.Send(copy, new AnswerCall(h.Mission.Id, call.CallId, option));
        }
    }

    [Fact]
    public void A_site_that_cannot_be_filled_is_a_defect_naming_the_assignment()
    {
        ContentPack empty = TestContent.Pack with { Objectives = [] };
        var bus = new EventBus();
        CycleModule.Register(bus);
        CrewModule.Register(bus, empty);
        MissionsModule.Register(bus, empty);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };

        var error = Assert.Throws<InvalidOperationException>(() => bus.Send(state, new FoundLegacy()));

        Assert.Contains(TestContent.Pack.Assignments[0].Id, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_team_is_refused_when_no_site_has_arrived()
    {
        var bus = new EventBus();
        CycleModule.Register(bus);
        CrewModule.Register(bus, TestContent.Pack);
        MissionsModule.Register(bus, TestContent.Pack);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1, Phase = CyclePhase.Briefing };
        state.Module<CycleState>("cycle").Founded = true;

        var refusal = Assert.Throws<CommandRejectedException>(() => bus.Send(state, new AssignTeam("o_1_1", Pair, "CREW_01", [], new Parameters())));

        Assert.Equal("No site has arrived.", refusal.Message);
    }

    [Fact]
    public void Phases_entered_without_a_site_do_nothing()
    {
        var bus = NewBus();
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };
        state.Module<CycleState>("cycle").Founded = true;
        state.Phase = CyclePhase.Resolution;

        bus.Send(state, new AdvancePhase());
        bus.Send(state, new AdvancePhase());

        Assert.Equal(CyclePhase.Advance, state.Phase);
    }

    [Fact]
    public void An_answer_with_no_mission_waiting_is_refused()
    {
        Harness h = ToBriefing();

        Assert.Contains("No mission", h.Refused(new AnswerCall("m_1_1", "CALL_1", CallAnswer.Safe)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mission_beats_are_indexed_without_a_gap_across_the_pause_and_the_mission_ends_last_and_once()
    {
        Harness h = WaitingOnCall();
        h.Finish();

        List<MissionBeat> beats = h.Events.Select(e => e.Event).OfType<MissionBeat>().ToList();
        List<IEvent> mission = h.Events.Select(e => e.Event).Where(e => e is MissionStarted or MissionBeat or MissionEnded).ToList();

        Assert.Equal(Enumerable.Range(0, beats.Count), beats.Select(b => b.Index));
        Assert.Equal(h.Mission!.BeatCount, beats.Count);
        Assert.IsType<MissionStarted>(mission[0]);
        Assert.IsType<MissionEnded>(mission[^1]);
        Assert.Single(mission.OfType<MissionEnded>());
        Assert.Contains(beats, b => b.Beat is CallBeat);
        Assert.IsType<ResultBeat>(beats[^1].Beat);
    }

    [Fact]
    public void The_mission_started_event_names_the_team_and_the_mission()
    {
        Harness h = WaitingOnCall();

        MissionStarted started = h.Events.Select(e => e.Event).OfType<MissionStarted>().Single();

        Assert.Equal("m_1_1", started.MissionId);
        Assert.Equal(h.FirstObjectiveId, started.ObjectiveId);
        Assert.Equal(Pair, started.CrewIds);
        Assert.Equal("CREW_01", started.LeadId);
        Assert.Equal(started.MissionId, h.Mission!.Id);
    }

    [Fact]
    public void The_crew_module_applies_the_result_after_the_mission_ends()
    {
        Harness h = WaitingOnCall();
        h.Finish();

        CrewState crew = h.State.Module<CrewState>("crew");

        Assert.Equal(1, crew.Members.Single(m => m.Id == "CREW_01").AwayMissions);
        Assert.Equal(1, crew.Members.Single(m => m.Id == "CREW_02").AwayMissions);
        Assert.Equal(0, crew.Members.Single(m => m.Id == "CREW_03").AwayMissions);
        Assert.NotNull(h.Mission!.State.Result);
    }

    [Fact]
    public void A_legacy_with_a_call_waiting_round_trips_per_module_and_continues_to_the_same_events()
    {
        Harness original = WaitingOnCall();
        LegacyState restored = Restore(original);

        EventBus bus = NewBus();
        List<string> Run(EventBus b, LegacyState state)
        {
            var log = new List<PublishedEvent>();
            PendingCall call = state.Module<MissionsState>("missions").Site!.Mission!.State.Pending!;
            string id = state.Module<MissionsState>("missions").Site!.Mission!.Id;
            log.AddRange(b.Send(state, new AnswerCall(id, call.CallId, call.LeadPrefers)));
            while (state.Module<MissionsState>("missions").Site!.Mission!.State.Pending is { } next)
            {
                log.AddRange(b.Send(state, new AnswerCall(id, next.CallId, next.LeadPrefers)));
            }

            for (int i = 0; i < 12; i++)
            {
                log.AddRange(b.Send(state, new AdvancePhase()));
            }

            return log.Select(e => JsonSerializer.Serialize(Registry.ToEnvelope(e))).ToList();
        }

        string before = JsonSerializer.Serialize(restored.Modules, SimJson.Options);
        Assert.Equal(JsonSerializer.Serialize(original.State.Modules, SimJson.Options), before);

        List<string> fromOriginal = Run(original.Bus, original.State);
        List<string> fromRestored = Run(bus, restored);

        Assert.Equal(fromOriginal, fromRestored);
        Assert.Equal(JsonSerializer.Serialize(original.State.Modules, SimJson.Options), JsonSerializer.Serialize(restored.Modules, SimJson.Options));
    }

    [Fact]
    public void A_stored_missions_state_is_read_strictly()
    {
        string json = JsonSerializer.Serialize(new MissionsState(), SimJson.Options);

        Assert.Null(JsonSerializer.Deserialize<MissionsState>(json, SimJson.Options)!.Site);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MissionsState>("{}", SimJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MissionsState>("""{"Site":null,"Extra":1}""", SimJson.Options));
    }

    [Fact]
    public void Two_missions_in_one_legacy_have_different_seeds()
    {
        Harness h = ToBriefing(5);
        h.Send(h.Assign(Pair));
        h.GoTo(CyclePhase.Resolution);
        ulong first = h.Mission!.State.Seed;
        h.Finish();
        h.GoTo(CyclePhase.Briefing);
        IRosterView roster = h.Bus.View<IRosterView>(h.State);
        string[] fit = roster.Roster.Where(roster.IsFit).Take(2).ToArray();
        h.Send(h.Assign(fit));
        h.GoTo(CyclePhase.Resolution);

        Assert.Equal("m_2_1", h.Mission!.Id);
        Assert.NotEqual(first, h.Mission.State.Seed);
        Assert.Equal(RngStream.For(5, 2, "mission:m_2_1").NextUInt64(), h.Mission.State.Seed);
        Assert.Equal(RngStream.For(5, 1, "mission:m_1_1").NextUInt64(), first);
    }
}

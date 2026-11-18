using GameHost;
using Sim.Core;
using Sim.Cycle;
using Sim.Missions;
using SimRunner;

namespace Web.Tests;

public sealed class ParityTests : IDisposable
{
    private const int Cycles = 6;
    private readonly TestApp app = new();
    private readonly HttpClient client;

    public ParityTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    // A legacy played through the requests stores exactly what the simulator's straight run publishes.
    // One cycle where the captain sends nobody, on both sides: the web run's /advance and the straight run's AdvancePhase.
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public async Task A_cycle_with_nobody_sent_has_the_same_event_log_too(ulong seed)
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", seed);
        var captain = new WebCaptain(app, client, id, SendNobodyInCycleTwo);
        for (int steps = 0; (await captain.Read()).State.Cycle <= Cycles; steps++)
        {
            Assert.True(steps < 5000, "The legacy did not finish its cycles.");
            await captain.Step();
        }

        IReadOnlyList<EventEnvelope> actual = app.Game.Store.ReadEvents(id);
        Assert.Equal(Straight(seed, SendNobodyInCycleTwo), actual);
        var assigned = actual.Where(e => e.Type == "TeamAssigned").Select(e => e.Cycle).ToList();
        Assert.DoesNotContain(2, assigned);
        Assert.Contains(1, assigned);
        Assert.Contains(3, assigned);
    }

    private ICommand? SendNobodyInCycleTwo(LegacyState state)
    {
        bool briefing = state.Phase == CyclePhase.Briefing && state.Cycle == 2 && state.TryModule(MissionsModule.Name, out MissionsState? missions) && missions.Site is { Briefing: null };
        return briefing ? new AdvancePhase() : null;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public async Task A_legacy_played_through_the_requests_has_the_simulators_event_log(ulong seed)
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", seed);
        var captain = new WebCaptain(app, client, id);
        var routes = new HashSet<string>();
        for (int steps = 0; (await captain.Read()).State.Cycle <= Cycles; steps++)
        {
            Assert.True(steps < 5000, "The legacy did not finish its cycles.");
            routes.Add((await captain.Step()).Route);
        }

        Assert.Equal(new[] { "advance", "answer", "launch" }, routes.Order());

        IReadOnlyList<EventEnvelope> expected = Straight(seed);
        IReadOnlyList<EventEnvelope> actual = app.Game.Store.ReadEvents(id);
        Assert.True(expected.Count > 100, "The run is too short to prove anything.");
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i] == actual[i], $"Envelope {i} differs: expected {expected[i]}, got {actual[i]}.");
        }
    }

    // The fixture's storylets arrive about 0.2 times in 100 cycles, so this copy is edited as tests/browser/play.sh edits it:
    // STORYLET_01 holds while trait_a is 5 or less, so it is offered in the first aftermath and answered there.
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public async Task A_legacy_that_answers_storylets_through_the_requests_has_the_simulators_event_log(ulong seed)
    {
        using var storied = new TestApp(scratch: true, prepare: root =>
        {
            string path = Path.Combine(root, "storylets", "base.yaml");
            string text = File.ReadAllText(path);
            foreach ((string old, string now) in new[]
            {
                ("- { trait: trait_a, min: 1, max: 1 }", "- { trait: trait_a, max: 5 }"),
                ("effects: [{ trait: trait_a, set: 0 }, { loyalty: -1, on: crew }]", "effects: [{ trait: trait_a, set: 9 }, { loyalty: -1, on: crew }]"),
                ("effects: [{ trait: trait_a, set: 2 }, { trait: trait_f, add: 1 }]", "effects: [{ trait: trait_a, set: 9 }, { trait: trait_f, add: 1 }]"),
            })
            {
                Assert.Contains(old, text);
                text = text.Replace(old, now, StringComparison.Ordinal);
            }

            File.WriteAllText(path, text);
        });
        HttpClient storiedClient = storied.NewClient();
        Guid id = await storied.Sessions.CreateLegacy(storied.Live, "LEGACY", seed);
        var captain = new WebCaptain(storied, storiedClient, id);
        for (int steps = 0; (await captain.Read()).State.Cycle <= Cycles; steps++)
        {
            Assert.True(steps < 5000, "The legacy did not finish its cycles.");
            await captain.Step();
        }

        IReadOnlyList<EventEnvelope> expected = Straight(seed, on: storied);
        IReadOnlyList<EventEnvelope> actual = storied.Game.Store.ReadEvents(id);
        Assert.Contains(actual, e => e.Type == "StoryletAnswered");
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i] == actual[i], $"Envelope {i} differs: expected {expected[i]}, got {actual[i]}.");
        }
    }

    private IReadOnlyList<EventEnvelope> Straight(ulong seed, Func<LegacyState, ICommand?>? choose = null, TestApp? on = null)
    {
        TestApp app = on ?? this.app;
        Game game = Game.Build(app.Live.Pack);
        var state = new LegacyState { LegacyId = new Guid((int)(seed >> 32), 0, 0, BitConverter.GetBytes(seed)), Seed = seed };
        var events = new List<PublishedEvent>();
        while (state.Cycle <= Cycles)
        {
            if (choose is null)
            {
                events.AddRange(AutoCaptain.PlayCycle(game, state, app.Live.Pack));
            }
            else
            {
                events.AddRange(game.Bus.Send(state, choose(state) ?? AutoCaptain.NextCommand(game, state, app.Live.Pack)));
            }
        }

        return events.Select(game.Events.ToEnvelope).ToList();
    }
}

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
        Guid id = await app.Sessions.CreateLegacy("LEGACY", seed);
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
        Guid id = await app.Sessions.CreateLegacy("LEGACY", seed);
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

    private IReadOnlyList<EventEnvelope> Straight(ulong seed, Func<LegacyState, ICommand?>? choose = null)
    {
        Game game = Game.Build(app.Game.Pack);
        var state = new LegacyState { LegacyId = new Guid((int)(seed >> 32), 0, 0, BitConverter.GetBytes(seed)), Seed = seed };
        var events = new List<PublishedEvent>();
        while (state.Cycle <= Cycles)
        {
            if (choose is null)
            {
                events.AddRange(AutoCaptain.PlayCycle(game, state, app.Game.Pack));
            }
            else
            {
                events.AddRange(game.Bus.Send(state, choose(state) ?? AutoCaptain.NextCommand(game, state, app.Game.Pack)));
            }
        }

        return events.Select(game.Events.ToEnvelope).ToList();
    }
}

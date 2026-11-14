using GameHost;
using Persistence;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using SimRunner;

namespace SimRunner.Tests;

public sealed class EndToEndTests : IDisposable
{
    private const ulong Seed = 1;

    private const int Cycles = 6;

    private static readonly ContentPack Pack = TestPack.Load();

    private readonly string path = Path.Combine(Path.GetTempPath(), $"starship-sim-e2e-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    private static bool CallIsWaiting(LegacyState state)
    {
        return state.Modules.TryGetValue("missions", out object? module) && ((MissionsState)module).Site?.Mission?.State.Pending is not null;
    }

    [Fact]
    public void A_legacy_stored_and_reloaded_after_every_command_plays_out_the_same_as_an_uninterrupted_run()
    {
        Game game = Game.Build(Pack);

        LegacyState straight = TestPack.NewLegacy(Seed);
        var expected = new List<EventEnvelope>();
        while (straight.Cycle <= Cycles)
        {
            expected.AddRange(AutoCaptain.Step(game, straight, Pack).Select(game.Events.ToEnvelope));
        }

        var store = new SqliteLegacyStore($"Data Source={path}");
        LegacyState state = TestPack.NewLegacy(Seed);
        Guid id = state.LegacyId;
        store.Create(new LegacyRecord(state, Pack.Id, Pack.Version));
        int stepsWhileWaiting = 0;
        while (state.Cycle <= Cycles)
        {
            if (CallIsWaiting(state))
            {
                stepsWhileWaiting++;
            }

            IReadOnlyList<EventEnvelope> events = AutoCaptain.Step(game, state, Pack).Select(game.Events.ToEnvelope).ToList();
            store.Commit(state, events);
            state = store.Load(id, game.ModuleTypes)!.State;
        }

        Assert.True(stepsWhileWaiting > 0, "No step was taken while a call was waiting; choose another seed.");
        Assert.Equal(expected, store.ReadEvents(id));
        Assert.Equal(straight.Cycle, state.Cycle);
    }
}

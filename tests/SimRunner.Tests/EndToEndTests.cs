using System.Text.Json;
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
        return state.TryModule(MissionsModule.Name, out MissionsState? missions) && missions.Site?.Mission?.State.Pending is not null;
    }

    private static string Books(LegacyState state)
    {
        return JsonSerializer.Serialize(new SortedDictionary<string, object>(state.Modules, StringComparer.Ordinal), SimJson.Options);
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
        store.Create(new LegacyRecord(state, Pack.Id, Pack.Version, "LEGACY", new DateTimeOffset(2026, 11, 14, 8, 30, 0, TimeSpan.Zero)), []);
        int stepsWhileWaiting = 0;
        while (state.Cycle <= Cycles)
        {
            if (CallIsWaiting(state))
            {
                stepsWhileWaiting++;
            }

            long sequenceLoadedAt = state.NextSequence;
            IReadOnlyList<EventEnvelope> events = AutoCaptain.Step(game, state, Pack).Select(game.Events.ToEnvelope).ToList();
            store.Commit(state, events, sequenceLoadedAt);
            state = store.Load(id, game.ModuleTypes)!.State;
        }

        Assert.True(stepsWhileWaiting > 0, "No step was taken while a call was waiting; choose another seed.");
        Assert.Equal(expected, store.ReadEvents(id));
        Assert.Equal(straight.Cycle, state.Cycle);
        Assert.Equal(Books(straight), Books(state));
        Assert.Equal(straight.RngStates.OrderBy(r => r.Key, StringComparer.Ordinal), state.RngStates.OrderBy(r => r.Key, StringComparer.Ordinal));
    }
}

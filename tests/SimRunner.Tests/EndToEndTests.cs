using System.Text.Json;
using GameHost;
using Persistence;
using Sim.Chronicle;
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

    // Focus 1 (design 4.10): an entry's text is written once, with the pack live when it happens, and never again. Entries are written
    // and committed under the fixture; then every chronicle line is rewritten in a copy of the pack, and the legacy is loaded under
    // it, both from the store and from an export, and played on. The old entries keep their words, and the new ones take the new lines.
    [Fact]
    public void A_chronicle_entry_keeps_its_words_when_the_lines_change_across_a_reload_and_an_export()
    {
        var store = new SqliteLegacyStore($"Data Source={path}");
        Game game = Game.Build(Pack);
        LegacyState state = TestPack.NewLegacy(Seed);
        Guid id = state.LegacyId;
        store.Create(new LegacyRecord(state, Pack.Id, Pack.Version, "LEGACY", new DateTimeOffset(2026, 11, 14, 8, 30, 0, TimeSpan.Zero)), []);
        while (state.Cycle <= 3)
        {
            long sequenceLoadedAt = state.NextSequence;
            store.Commit(state, AutoCaptain.Step(game, state, Pack).Select(game.Events.ToEnvelope).ToList(), sequenceLoadedAt);
        }

        List<(string Id, string Text)> written = Words(game.Bus.View<IChronicleView>(state).Entries);
        string json = LegacyExport.Export(store, id, game.ModuleTypes);

        ContentPack rewritten = Pack with
        {
            Lines = Pack.Lines.Select(l => l.Beat.StartsWith("chronicle_", StringComparison.Ordinal) ? l with { Text = "REWRITTEN " + l.Text } : l).ToList(),
        };
        Game later = Game.Build(rewritten);
        LegacyState reloaded = store.Load(id, later.ModuleTypes)!.State;
        string imported = Path.ChangeExtension(path, ".imported.db");
        try
        {
            var other = new SqliteLegacyStore($"Data Source={imported}");
            LegacyExport.Import(other, json, later.ModuleTypes);
            Assert.Equal(written, Words(later.Bus.View<IChronicleView>(other.Load(id, later.ModuleTypes)!.State).Entries));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(imported);
        }

        while (reloaded.Cycle <= 5)
        {
            long sequenceLoadedAt = reloaded.NextSequence;
            store.Commit(reloaded, AutoCaptain.Step(later, reloaded, rewritten).Select(later.Events.ToEnvelope).ToList(), sequenceLoadedAt);
            reloaded = store.Load(id, later.ModuleTypes)!.State;
        }

        List<(string Id, string Text)> after = Words(later.Bus.View<IChronicleView>(reloaded).Entries);
        Assert.NotEmpty(written);
        Assert.DoesNotContain(written, e => e.Text.StartsWith("REWRITTEN ", StringComparison.Ordinal));
        Assert.Equal(written, after.Take(written.Count));
        Assert.True(after.Count > written.Count, "No entry was written after the reload.");
        Assert.All(after.Skip(written.Count), e => Assert.StartsWith("REWRITTEN ", e.Text, StringComparison.Ordinal));
    }

    private static List<(string Id, string Text)> Words(IReadOnlyList<ChronicleEntry> entries)
    {
        return entries.Select(e => (e.Id, e.Text)).ToList();
    }
}

using Persistence;
using Sim.Core;

namespace Persistence.Tests;

public sealed class SqliteLegacyStoreTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"starship-sim-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    private SqliteLegacyStore NewStore()
    {
        return new SqliteLegacyStore($"Data Source={path}");
    }

    private static LegacyRecord NewRecord(Guid id)
    {
        // A seed above long.MaxValue checks that the full unsigned range survives storage.
        return new LegacyRecord(new LegacyState { LegacyId = id, Seed = ulong.MaxValue - 5 }, "CORE", 1);
    }

    [Fact]
    public void A_committed_legacy_loads_back_identically()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Cycle = 4;
        record.State.Phase = CyclePhase.Aftermath;
        record.State.NextSequence = 3;
        record.State.RngStates["4:missions"] = ulong.MaxValue;
        var events = new[] { new EventEnvelope(1, 3, "A", 1, "{}"), new EventEnvelope(2, 4, "B", 2, """{"X":1}""") };

        store.Commit(record.State, events);

        LegacyRecord loaded = NewStore().Load(id)!;
        Assert.Equal(ulong.MaxValue - 5, loaded.State.Seed);
        Assert.Equal(4, loaded.State.Cycle);
        Assert.Equal(CyclePhase.Aftermath, loaded.State.Phase);
        Assert.Equal(3, loaded.State.NextSequence);
        Assert.Equal(ulong.MaxValue, loaded.State.RngStates["4:missions"]);
        Assert.Equal("CORE", loaded.ContentPackId);
        Assert.Equal(1, loaded.ContentPackVersion);
        Assert.Equal(events, NewStore().ReadEvents(id));
    }

    [Fact]
    public void A_missing_legacy_loads_as_null()
    {
        Assert.Null(NewStore().Load(Guid.NewGuid()));
    }

    // One player action is one transaction: a failed event insert must not leave the state half saved.
    [Fact]
    public void A_failed_commit_changes_nothing()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")]);
        record.State.Cycle = 9;

        Assert.ThrowsAny<Exception>(() => store.Commit(record.State, [new EventEnvelope(1, 1, "Duplicate", 1, "{}")]));

        Assert.Equal(1, store.Load(id)!.State.Cycle);
        Assert.Single(store.ReadEvents(id));
    }

    // Legacies are the isolation boundary: one legacy's events never appear in another's.
    [Fact]
    public void Events_are_isolated_by_legacy()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(first));
        store.Create(NewRecord(second));
        store.Commit(NewRecord(first).State, [new EventEnvelope(1, 1, "A", 1, "{}")]);

        Assert.Empty(store.ReadEvents(second));
    }

    [Fact]
    public void An_exported_legacy_imports_into_an_empty_database()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Cycle = 2;
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")]);
        string json = LegacyExport.Export(store, id);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        SqliteLegacyStore empty = NewStore();
        Guid imported = LegacyExport.Import(empty, json);

        Assert.Equal(id, imported);
        Assert.Equal(2, empty.Load(id)!.State.Cycle);
        Assert.Equal(json, LegacyExport.Export(empty, id));
    }
}

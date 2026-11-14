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

    private sealed class FuelState
    {
        public int Units { get; set; }
    }

    private sealed class LogState
    {
        public List<string> Lines { get; init; } = new();
    }

    private static readonly IReadOnlyDictionary<string, Type> ModuleTypes = new Dictionary<string, Type>
    {
        ["fuel"] = typeof(FuelState),
        ["log"] = typeof(LogState),
    };

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
        record.State.Week = 7;
        record.State.RngStates["4:missions"] = ulong.MaxValue;
        record.State.Module<FuelState>("fuel").Units = 12;
        record.State.Module<LogState>("log").Lines.Add("CREW_01");
        var events = new[] { new EventEnvelope(1, 3, "A", 1, "{}"), new EventEnvelope(2, 4, "B", 2, """{"X":1}""") };

        store.Commit(record.State, events);

        LegacyRecord loaded = NewStore().Load(id, ModuleTypes)!;
        Assert.Equal(ulong.MaxValue - 5, loaded.State.Seed);
        Assert.Equal(4, loaded.State.Cycle);
        Assert.Equal(CyclePhase.Aftermath, loaded.State.Phase);
        Assert.Equal(3, loaded.State.NextSequence);
        Assert.Equal(7, loaded.State.Week);
        Assert.Equal(12, loaded.State.Module<FuelState>("fuel").Units);
        Assert.Equal(["CREW_01"], loaded.State.Module<LogState>("log").Lines);
        Assert.Equal(ulong.MaxValue, loaded.State.RngStates["4:missions"]);
        Assert.Equal("CORE", loaded.ContentPackId);
        Assert.Equal(1, loaded.ContentPackVersion);
        Assert.Equal(events, NewStore().ReadEvents(id));
    }

    [Fact]
    public void A_missing_legacy_loads_as_null()
    {
        Assert.Null(NewStore().Load(Guid.NewGuid(), ModuleTypes));
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
        record.State.Module<FuelState>("fuel").Units = 99;

        Assert.ThrowsAny<Exception>(() => store.Commit(record.State, [new EventEnvelope(1, 1, "Duplicate", 1, "{}")]));

        Assert.Equal(1, store.Load(id, ModuleTypes)!.State.Cycle);
        Assert.Empty(store.Load(id, ModuleTypes)!.State.Modules);
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
        store.Commit(NewRecord(second).State, [new EventEnvelope(1, 1, "B", 1, "{}")]);

        Assert.Equal("A", Assert.Single(store.ReadEvents(first)).Type);
        Assert.Equal("B", Assert.Single(store.ReadEvents(second)).Type);
    }

    [Fact]
    public void An_exported_legacy_imports_into_an_empty_database()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Cycle = 2;
        record.State.Module<FuelState>("fuel").Units = 5;
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")]);
        string json = LegacyExport.Export(store, id, ModuleTypes);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        SqliteLegacyStore empty = NewStore();
        Guid imported = LegacyExport.Import(empty, json, ModuleTypes);

        Assert.Equal(id, imported);
        Assert.Equal(2, empty.Load(id, ModuleTypes)!.State.Cycle);
        Assert.Equal(5, empty.Load(id, ModuleTypes)!.State.Module<FuelState>("fuel").Units);
        Assert.Equal(json, LegacyExport.Export(empty, id, ModuleTypes));
    }

    [Fact]
    public void An_import_with_an_unknown_or_missing_format_version_is_rejected()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id));
        string json = LegacyExport.Export(store, id, ModuleTypes);

        Assert.Contains("\"FormatVersion\": 2", json);
        Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(store, json.Replace("\"FormatVersion\": 2", "\"FormatVersion\": 1"), ModuleTypes));
        Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(store, json.Replace("\"FormatVersion\": 2,", ""), ModuleTypes));
    }

    [Fact]
    public void A_stored_module_that_is_not_declared_fails_with_its_name()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Module<FuelState>("fuel").Units = 1;
        store.Commit(record.State, []);

        var ex = Assert.Throws<InvalidOperationException>(() => store.Load(id, new Dictionary<string, Type>()));

        Assert.Contains("fuel", ex.Message);
    }

    [Fact]
    public void A_stored_module_that_does_not_read_strictly_fails_with_its_name()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO module_state (legacy_id, module, json) VALUES ('{id}', 'fuel', '{{\"Gone\":1}}')";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => store.Load(id, ModuleTypes));

        Assert.Contains("fuel", ex.Message);
    }

    [Fact]
    public void A_database_from_an_older_schema_is_refused()
    {
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE legacy (legacy_id TEXT PRIMARY KEY); PRAGMA user_version = 1;";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => NewStore());

        Assert.Equal("This database is from an older build (schema 1). Delete it or export its legacies with that build.", ex.Message);
    }

    [Fact]
    public void A_new_database_is_created_at_schema_version_2()
    {
        NewStore();

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        connection.Open();
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(2L, command.ExecuteScalar());
    }
}

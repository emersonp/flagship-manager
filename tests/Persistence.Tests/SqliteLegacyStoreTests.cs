using Persistence;
using Sim.Core;
using Sim.Crew;

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
    public void Crew_state_survives_the_store()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Module<CrewState>("crew").Members.Add(new CrewMember
        {
            Id = "CREW_01",
            Seniority = 2,
            Skills = new Dictionary<string, SkillState> { ["SKILL_A"] = new SkillState { Level = 3, Xp = 1.5 } },
            Caution = 1,
            Discipline = -1,
            Status = CrewStatus.Recovering,
            LightInjuriesHealAtWeek = new List<int> { 4 },
            ReturnsAtWeek = 9,
            Loyalty = 55,
            AwayMissions = 2,
        });
        store.Commit(record.State, Array.Empty<EventEnvelope>());

        var types = new Dictionary<string, Type> { ["crew"] = typeof(CrewState) };
        CrewMember loaded = NewStore().Load(id, types)!.State.Module<CrewState>("crew").Members.Single();

        Assert.Equal(CrewStatus.Recovering, loaded.Status);
        Assert.Equal(9, loaded.ReturnsAtWeek);
        Assert.Equal([4], loaded.LightInjuriesHealAtWeek);
        Assert.Equal(1.5, loaded.Skills["SKILL_A"].Xp);
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
        Assert.Equal(id, loaded.State.LegacyId);
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
        record.State.Phase = CyclePhase.Briefing;
        record.State.Week = 9;
        record.State.NextSequence = 2;
        record.State.RngStates["2:missions"] = 77;
        record.State.Module<FuelState>("fuel").Units = 5;
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")]);
        string json = LegacyExport.Export(store, id, ModuleTypes);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        SqliteLegacyStore empty = NewStore();
        Guid imported = LegacyExport.Import(empty, json, ModuleTypes);

        Assert.Equal(id, imported);
        LegacyState state = empty.Load(id, ModuleTypes)!.State;
        Assert.Equal(ulong.MaxValue - 5, state.Seed);
        Assert.Equal(2, state.Cycle);
        Assert.Equal(CyclePhase.Briefing, state.Phase);
        Assert.Equal(9, state.Week);
        Assert.Equal(2, state.NextSequence);
        Assert.Equal(77UL, state.RngStates["2:missions"]);
        Assert.Equal(5, state.Module<FuelState>("fuel").Units);
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

    // Load must not skip rows it cannot type.
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

    // An unknown member in stored JSON is a refusal, not a silent drop.
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

    // Version 0 is the phase 0 database, which had a legacy table and never set a version.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_database_from_an_older_schema_is_refused(int version)
    {
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE legacy (legacy_id TEXT PRIMARY KEY); PRAGMA user_version = {version};";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => NewStore());

        Assert.Equal($"This database is from an older build (schema {version}). Delete it or export its legacies with that build.", ex.Message);
    }

    // A fresh database must carry the version later builds check.
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

    // Every module the state no longer holds is dropped from storage with the commit.
    [Fact]
    public void A_module_removed_from_the_state_is_deleted_on_commit()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Module<FuelState>("fuel").Units = 1;
        record.State.Module<LogState>("log");
        store.Commit(record.State, []);
        record.State.Modules.Remove("log");

        store.Commit(record.State, []);

        Assert.Equal(["fuel"], store.Load(id, ModuleTypes)!.State.Modules.Keys);
    }

    // A snapshot module no build declares must not be silently dropped.
    [Fact]
    public void An_import_with_an_undeclared_module_is_refused_by_name()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Module<FuelState>("fuel").Units = 1;
        store.Commit(record.State, []);
        string json = LegacyExport.Export(store, id, ModuleTypes);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);

        var ex = Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(NewStore(), json, new Dictionary<string, Type>()));

        Assert.Contains("fuel", ex.Message);
    }

    // The snapshot type allows nulls, so a missing collection needs its own refusal.
    [Theory]
    [InlineData("Modules")]
    [InlineData("RngStates")]
    public void An_import_missing_a_required_collection_is_refused_by_name(string member)
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id));
        var node = System.Text.Json.Nodes.JsonNode.Parse(LegacyExport.Export(store, id, ModuleTypes))!;
        node["Legacy"]!.AsObject().Remove(member);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);

        var ex = Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(NewStore(), node.ToJsonString(), ModuleTypes));

        Assert.Contains(member, ex.Message);
    }

    // A field added to LegacyState must be carried by the export before this passes.
    [Fact]
    public void The_export_snapshot_carries_every_legacy_state_property()
    {
        string[] carried = typeof(LegacyExport).Assembly.GetType("Persistence.SnapshotLegacy")!
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();
        string[] stateProperties = typeof(LegacyState)
            .GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToArray();

        Assert.All(stateProperties, name => Assert.Contains(name, carried));
    }
}

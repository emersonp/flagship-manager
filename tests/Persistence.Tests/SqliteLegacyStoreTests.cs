using System.Text.Json;
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

    private static readonly DateTimeOffset Created = new(2026, 11, 14, 8, 30, 0, TimeSpan.Zero);

    private static LegacyRecord NewRecord(Guid id)
    {
        // A seed above long.MaxValue checks that the full unsigned range survives storage.
        return new LegacyRecord(new LegacyState { LegacyId = id, Seed = ulong.MaxValue - 5 }, "CORE", 1, "LEGACY", Created);
    }

    [Fact]
    public void Crew_state_survives_the_store()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
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
            Morale = 31,
            LastSentCycle = 3,
        });
        store.Commit(record.State, Array.Empty<EventEnvelope>(), 1);

        var types = new Dictionary<string, Type> { ["crew"] = typeof(CrewState) };
        CrewMember loaded = NewStore().Load(id, types)!.State.Module<CrewState>("crew").Members.Single();

        Assert.Equal(CrewStatus.Recovering, loaded.Status);
        Assert.Equal(9, loaded.ReturnsAtWeek);
        Assert.Equal([4], loaded.LightInjuriesHealAtWeek);
        Assert.Equal(1.5, loaded.Skills["SKILL_A"].Xp);
        Assert.Equal((31, 3), (loaded.Morale, loaded.LastSentCycle));
    }

    [Fact]
    public void A_committed_legacy_loads_back_identically()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
        record.State.Cycle = 4;
        record.State.Phase = CyclePhase.Aftermath;
        record.State.NextSequence = 3;
        record.State.Week = 7;
        record.State.RngStates["4:missions"] = ulong.MaxValue;
        record.State.Module<FuelState>("fuel").Units = 12;
        record.State.Module<LogState>("log").Lines.Add("CREW_01");
        var events = new[] { new EventEnvelope(1, 3, "A", 1, "{}"), new EventEnvelope(2, 4, "B", 2, """{"X":1}""") };

        store.Commit(record.State, events, 1);

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
        store.Create(record, []);
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")], 1);
        record.State.Cycle = 9;
        record.State.Module<FuelState>("fuel").Units = 99;

        Assert.ThrowsAny<Exception>(() => store.Commit(record.State, [new EventEnvelope(1, 1, "Duplicate", 1, "{}")], 1));

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
        store.Create(NewRecord(first), []);
        store.Create(NewRecord(second), []);
        store.Commit(NewRecord(first).State, [new EventEnvelope(1, 1, "A", 1, "{}")], 1);
        store.Commit(NewRecord(second).State, [new EventEnvelope(1, 1, "B", 1, "{}")], 1);

        Assert.Equal("A", Assert.Single(store.ReadEvents(first)).Type);
        Assert.Equal("B", Assert.Single(store.ReadEvents(second)).Type);
    }

    [Fact]
    public void An_exported_legacy_imports_into_an_empty_database()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
        record.State.Cycle = 2;
        record.State.Phase = CyclePhase.Briefing;
        record.State.Week = 9;
        record.State.NextSequence = 2;
        record.State.RngStates["2:missions"] = 77;
        record.State.Module<FuelState>("fuel").Units = 5;
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")], 1);
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
        store.Create(NewRecord(id), []);
        string json = LegacyExport.Export(store, id, ModuleTypes);

        Assert.Contains("\"FormatVersion\": 7", json);
        Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(store, json.Replace("\"FormatVersion\": 7", "\"FormatVersion\": 1"), ModuleTypes));
        Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(store, json.Replace("\"FormatVersion\": 7,", ""), ModuleTypes));
    }

    // Name and creation time are record facts, stored as given (time as a UTC instant).
    [Fact]
    public void A_legacys_name_and_creation_time_round_trip()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        var local = new DateTimeOffset(2026, 11, 14, 3, 30, 0, TimeSpan.FromHours(-5));
        store.Create(new LegacyRecord(new LegacyState { LegacyId = id, Seed = 1 }, "CORE", 1, "  SHIP_NAME  ", local), []);

        LegacyRecord loaded = NewStore().Load(id, ModuleTypes)!;

        Assert.Equal("SHIP_NAME", loaded.Name);
        Assert.Equal(local, loaded.CreatedAt);
        Assert.Equal(TimeSpan.Zero, loaded.CreatedAt.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string name)
    {
        Assert.Throws<InvalidLegacyNameException>(() => NewStore().Create(NewRecord(Guid.NewGuid()) with { Name = name }, []));
    }

    [Fact]
    public void A_name_is_limited_to_sixty_characters_after_trimming()
    {
        SqliteLegacyStore store = NewStore();

        Assert.Throws<InvalidLegacyNameException>(() => store.Create(NewRecord(Guid.NewGuid()) with { Name = new string('A', 61) }, []));
        store.Create(NewRecord(Guid.NewGuid()) with { Name = " " + new string('A', 60) + " " }, []);
    }

    [Fact]
    public void Legacies_list_newest_first_with_their_current_week_and_cycle()
    {
        Guid older = Guid.NewGuid();
        Guid newer = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(older) with { Name = "OLDER", CreatedAt = Created }, []);
        store.Create(NewRecord(newer) with { Name = "NEWER", CreatedAt = Created.AddHours(1) }, []);
        LegacyState state = store.Load(older, ModuleTypes)!.State;
        state.Cycle = 3;
        state.Week = 8;
        store.Commit(state, [], 1);

        IReadOnlyList<LegacyListing> listing = store.ListLegacies();

        Assert.Equal(
            [new LegacyListing(newer, "NEWER", Created.AddHours(1), 1, 1), new LegacyListing(older, "OLDER", Created, 8, 3)],
            listing);
    }

    // Equal creation times fall back to the id so the order is stable.
    [Fact]
    public void Legacies_created_at_the_same_time_list_by_id()
    {
        Guid[] ids = [Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("00000000-0000-0000-0000-000000000001")];
        SqliteLegacyStore store = NewStore();
        foreach (Guid id in ids)
        {
            store.Create(NewRecord(id), []);
        }

        Assert.Equal(ids.OrderBy(i => i.ToString()), store.ListLegacies().Select(l => l.LegacyId));
    }

    [Fact]
    public void A_commit_at_a_stale_sequence_is_refused_and_writes_nothing()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
        record.State.NextSequence = 2;
        record.State.Module<FuelState>("fuel").Units = 4;
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}"), new EventEnvelope(2, 1, "B", 1, "{}")], 1);
        LegacyState stale = store.Load(id, ModuleTypes)!.State;
        stale.Cycle = 9;
        stale.NextSequence = 3;
        stale.Module<FuelState>("fuel").Units = 99;

        var ex = Assert.Throws<StaleLegacyException>(() => store.Commit(stale, [new EventEnvelope(3, 9, "C", 1, "{}")], 1));

        Assert.Contains(id.ToString(), ex.Message);
        LegacyState kept = store.Load(id, ModuleTypes)!.State;
        Assert.Equal(1, kept.Cycle);
        Assert.Equal(2, kept.NextSequence);
        Assert.Equal(4, kept.Module<FuelState>("fuel").Units);
        Assert.Equal(["A", "B"], store.ReadEvents(id).Select(e => e.Type));
    }

    [Fact]
    public void Events_read_by_cycle_in_sequence_order()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
        store.Commit(
            record.State,
            [new EventEnvelope(3, 2, "C", 1, "{}"), new EventEnvelope(1, 1, "A", 1, "{}"), new EventEnvelope(2, 2, "B", 1, "{}")],
            1);

        Assert.Equal(["B", "C"], store.ReadEvents(id, 2).Select(e => e.Type));
        Assert.Empty(store.ReadEvents(id, 7));
    }

    // Format 3 held the stored events from before authored approaches, format 4 a mission result with no flags, format 5 no storylets, and format 6 no morale.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void An_export_at_an_earlier_format_is_refused(int format)
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id), []);
        string json = LegacyExport.Export(store, id, ModuleTypes);

        Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(store, json.Replace("\"FormatVersion\": 7", $"\"FormatVersion\": {format}"), ModuleTypes));
    }

    [Fact]
    public void An_import_carries_the_name_and_creation_time()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id), []);
        string json = LegacyExport.Export(store, id, ModuleTypes);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        SqliteLegacyStore empty = NewStore();

        LegacyExport.Import(empty, json, ModuleTypes);

        LegacyRecord loaded = empty.Load(id, ModuleTypes)!;
        Assert.Equal("LEGACY", loaded.Name);
        Assert.Equal(Created, loaded.CreatedAt);
    }

    // Load must not skip rows it cannot type.
    [Fact]
    public void A_stored_module_that_is_not_declared_fails_with_its_name()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
        record.State.Module<FuelState>("fuel").Units = 1;
        store.Commit(record.State, [], 1);

        var ex = Assert.Throws<InvalidOperationException>(() => store.Load(id, new Dictionary<string, Type>()));

        Assert.Contains("fuel", ex.Message);
    }

    // An unknown member in stored JSON is a refusal, not a silent drop.
    [Fact]
    public void A_stored_module_that_does_not_read_strictly_fails_with_its_name()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id), []);
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
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void A_database_from_another_schema_is_refused(int version)
    {
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE legacy (legacy_id TEXT PRIMARY KEY); PRAGMA user_version = {version};";
            command.ExecuteNonQuery();
        }

        var ex = Assert.Throws<InvalidOperationException>(() => NewStore());

        Assert.Equal($"This database is from a different build (schema {version}, this build reads 9). Delete it or export its legacies with the build that wrote it.", ex.Message);
    }

    // A phase stored as a number would parse as a member by accident; the store refuses it.
    [Fact]
    public void A_stored_numeric_phase_is_refused()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id), []);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE legacy SET phase = '3'";
            command.ExecuteNonQuery();
        }

        Assert.ThrowsAny<Exception>(() => store.Load(id, ModuleTypes));
    }

    // A fresh database must carry the version later builds check.
    [Fact]
    public void A_new_database_is_created_at_schema_version_9()
    {
        NewStore();

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        connection.Open();
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(9L, command.ExecuteScalar());
    }

    // Every module the state no longer holds is dropped from storage with the commit.
    [Fact]
    public void A_module_removed_from_the_state_is_deleted_on_commit()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
        record.State.Module<FuelState>("fuel").Units = 1;
        record.State.Module<LogState>("log");
        store.Commit(record.State, [], 1);
        record.State.Modules.Remove("log");

        store.Commit(record.State, [], 1);

        Assert.Equal(["fuel"], store.Load(id, ModuleTypes)!.State.Modules.Keys);
    }

    // A snapshot module no build declares must not be silently dropped.
    [Fact]
    public void An_import_with_an_undeclared_module_is_refused_by_name()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
        record.State.Module<FuelState>("fuel").Units = 1;
        store.Commit(record.State, [], 1);
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
        store.Create(NewRecord(id), []);
        var node = System.Text.Json.Nodes.JsonNode.Parse(LegacyExport.Export(store, id, ModuleTypes))!;
        node["Legacy"]!.AsObject().Remove(member);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);

        var ex = Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(NewStore(), node.ToJsonString(), ModuleTypes));

        Assert.Contains(member, ex.Message);
    }

    // The envelope reads as strictly as every stored form: nothing is defaulted, dropped, or tolerated.
    [Theory]
    [InlineData("Legacy.Week")]
    [InlineData("Legacy.NextSequence")]
    [InlineData("Legacy.Seed")]
    [InlineData("Events.0.Sequence")]
    [InlineData("Events")]
    [InlineData("Legacy.Phase=3")]
    [InlineData("Legacy.Extra=1")]
    [InlineData("Extra=1")]
    [InlineData("Legacy=null")]
    [InlineData("Events.0.Payload=null")]
    public void An_import_that_does_not_read_strictly_is_refused_and_creates_nothing(string edit)
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record, []);
        record.State.NextSequence = 2;
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")], 1);
        var node = System.Text.Json.Nodes.JsonNode.Parse(LegacyExport.Export(store, id, ModuleTypes))!;
        string[] assign = edit.Split('=', 2);
        string[] path = assign[0].Split('.');
        System.Text.Json.Nodes.JsonNode parent = node;
        foreach (string step in path[..^1])
        {
            parent = int.TryParse(step, out int index) ? parent.AsArray()[index]! : parent[step]!;
        }

        if (assign.Length == 1)
        {
            parent.AsObject().Remove(path[^1]);
        }
        else
        {
            parent.AsObject()[path[^1]] = assign[1] switch
            {
                "null" => null,
                var number when int.TryParse(number, out int value) => value,
                _ => assign[1],
            };
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(this.path);
        SqliteLegacyStore empty = NewStore();

        Exception ex = Assert.ThrowsAny<Exception>(() => LegacyExport.Import(empty, node.ToJsonString(), ModuleTypes));

        Assert.True(ex is JsonException or InvalidOperationException, ex.GetType().Name);
        Assert.Null(empty.Load(id, ModuleTypes));
    }

    private long RowCount(string table)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
        connection.Open();
        using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public void Create_stores_the_events_with_the_legacy()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        record.State.Module<FuelState>("fuel").Units = 4;
        record.State.NextSequence = 3;

        store.Create(record, [new EventEnvelope(1, 1, "A", 1, "{}"), new EventEnvelope(2, 1, "B", 1, "{}")]);

        Assert.Equal(2, store.ReadEvents(id).Count);
        Assert.Equal(3, store.Load(id, ModuleTypes)!.State.NextSequence);
        Assert.Equal(4, store.Load(id, ModuleTypes)!.State.Module<FuelState>("fuel").Units);
    }

    [Fact]
    public void Create_with_colliding_events_stores_nothing_at_all()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        record.State.Module<FuelState>("fuel").Units = 4;

        Assert.ThrowsAny<Exception>(() => store.Create(record, [new EventEnvelope(1, 1, "A", 1, "{}"), new EventEnvelope(1, 1, "B", 1, "{}")]));

        Assert.Equal(0, RowCount("legacy"));
        Assert.Equal(0, RowCount("module_state"));
        Assert.Equal(0, RowCount("event_log"));
    }

    // A null payload passes the upfront checks and fails the NOT NULL column on the second event, after the legacy and module rows are in.
    [Fact]
    public void A_failure_inside_the_creation_transaction_rolls_back_every_table_and_the_id_can_be_reused()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        record.State.Module<FuelState>("fuel").Units = 4;
        record.State.NextSequence = 3;

        Assert.ThrowsAny<Exception>(() => store.Create(record, [new EventEnvelope(1, 1, "A", 1, "{}"), new EventEnvelope(2, 1, "B", 1, null!)]));

        Assert.Equal(0, RowCount("legacy"));
        Assert.Equal(0, RowCount("module_state"));
        Assert.Equal(0, RowCount("event_log"));
        store.Create(record, [new EventEnvelope(1, 1, "A", 1, "{}"), new EventEnvelope(2, 1, "B", 1, "{}")]);
        Assert.Equal(1, RowCount("legacy"));
        Assert.Equal(2, RowCount("event_log"));
    }

    [Fact]
    public void A_null_name_is_refused_as_a_bad_name()
    {
        Assert.Throws<InvalidLegacyNameException>(() => NewStore().Create(NewRecord(Guid.NewGuid()) with { Name = null! }, []));
    }

    // An import refuses a bad name the way it refuses everything else, with the store's words.
    [Fact]
    public void An_import_with_a_bad_name_is_refused_as_an_invalid_operation_and_stores_nothing()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id), []);
        var node = System.Text.Json.Nodes.JsonNode.Parse(LegacyExport.Export(store, id, ModuleTypes))!;
        node["Legacy"]!["Name"] = "   ";
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        SqliteLegacyStore empty = NewStore();

        var ex = Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(empty, node.ToJsonString(), ModuleTypes));

        Assert.Equal("A legacy name is 1 to 60 characters.", ex.Message);
        Assert.IsType<InvalidLegacyNameException>(ex.InnerException);
        Assert.Null(empty.Load(id, ModuleTypes));
    }

    // The events must run 1, 2, 3, and so on up to the state's last sequence: anything else is refused before a row is written.
    [Theory]
    [InlineData(3L, new long[] { 2, 3 })]
    [InlineData(3L, new long[] { 1, 3 })]
    [InlineData(4L, new long[] { 1, 2 })]
    [InlineData(2L, new long[] { 1, 2 })]
    [InlineData(2L, new long[] { })]
    [InlineData(1L, new long[] { 1 })]
    [InlineData(3L, new long[] { 2, 1 })]
    public void Create_refuses_events_that_do_not_run_from_one_to_the_states_last_sequence(long nextSequence, long[] sequences)
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        record.State.NextSequence = nextSequence;

        Assert.Throws<ArgumentException>(() => store.Create(record, sequences.Select(n => new EventEnvelope(n, 1, "A", 1, "{}")).ToList()));

        Assert.Equal(0, RowCount("legacy"));
        Assert.Equal(0, RowCount("event_log"));
    }

    [Theory]
    [InlineData(1L, new long[] { })]
    [InlineData(2L, new long[] { 1 })]
    [InlineData(4L, new long[] { 1, 2, 3 })]
    public void Create_accepts_events_that_run_from_one_to_the_states_last_sequence(long nextSequence, long[] sequences)
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        record.State.NextSequence = nextSequence;

        store.Create(record, sequences.Select(n => new EventEnvelope(n, 1, "A", 1, "{}")).ToList());

        Assert.Equal(sequences, store.ReadEvents(id).Select(e => e.Sequence));
    }

    // Name and creation time are required in the snapshot: neither is defaulted.
    [Theory]
    [InlineData("Name")]
    [InlineData("CreatedAt")]
    public void An_import_without_a_name_or_creation_time_is_refused_and_stores_nothing(string member)
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(id), []);
        var node = System.Text.Json.Nodes.JsonNode.Parse(LegacyExport.Export(store, id, ModuleTypes))!;
        Assert.True(node["Legacy"]!.AsObject().Remove(member));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        SqliteLegacyStore empty = NewStore();

        var ex = Assert.Throws<InvalidOperationException>(() => LegacyExport.Import(empty, node.ToJsonString(), ModuleTypes));

        Assert.Contains(member, ex.Message);
        Assert.Null(empty.Load(id, ModuleTypes));
        Assert.Equal(0, RowCount("legacy"));
    }

    [Fact]
    public void An_import_that_fails_part_way_stores_nothing_and_can_be_retried()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore source = NewStore();
        LegacyRecord record = NewRecord(id);
        record.State.Module<FuelState>("fuel").Units = 4;
        source.Create(record, []);
        record.State.NextSequence = 3;
        source.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}"), new EventEnvelope(2, 1, "B", 1, "{}")], 1);
        string good = LegacyExport.Export(source, id, ModuleTypes);
        string broken = good.Replace("\"Sequence\": 2", "\"Sequence\": 1");
        Assert.NotEqual(good, broken);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        SqliteLegacyStore empty = NewStore();

        Assert.ThrowsAny<Exception>(() => LegacyExport.Import(empty, broken, ModuleTypes));

        Assert.Equal(0, RowCount("legacy"));
        Assert.Equal(0, RowCount("module_state"));
        Assert.Equal(0, RowCount("event_log"));
        Assert.Equal(id, LegacyExport.Import(empty, good, ModuleTypes));
        Assert.Equal(2, empty.ReadEvents(id).Count);
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

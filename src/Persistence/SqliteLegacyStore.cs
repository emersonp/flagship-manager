using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sim.Core;

namespace Persistence;

public sealed class SqliteLegacyStore : ILegacyStore
{
    private const int MaxNameLength = 60;

    private const int SchemaVersion = 3;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS legacy (
            legacy_id TEXT PRIMARY KEY,
            seed TEXT NOT NULL,
            cycle INTEGER NOT NULL,
            phase TEXT NOT NULL,
            week INTEGER NOT NULL,
            next_sequence INTEGER NOT NULL,
            rng_states TEXT NOT NULL,
            content_pack_id TEXT NOT NULL,
            content_pack_version INTEGER NOT NULL,
            name TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS module_state (
            legacy_id TEXT NOT NULL,
            module TEXT NOT NULL,
            json TEXT NOT NULL,
            PRIMARY KEY (legacy_id, module)
        );
        CREATE TABLE IF NOT EXISTS event_log (
            legacy_id TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            cycle INTEGER NOT NULL,
            type TEXT NOT NULL,
            version INTEGER NOT NULL,
            payload TEXT NOT NULL,
            PRIMARY KEY (legacy_id, sequence)
        );
        """;

    private readonly string connectionString;

    public SqliteLegacyStore(string connectionString)
    {
        this.connectionString = connectionString;
        using SqliteConnection connection = Open();
        if (Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'legacy'") != 0)
        {
            long found = Scalar(connection, "PRAGMA user_version");
            if (found != SchemaVersion)
            {
                throw new InvalidOperationException($"This database is from a different build (schema {found}, this build reads {SchemaVersion}). Delete it or export its legacies with the build that wrote it.");
            }
        }

        Execute(connection, null, Schema);
        Execute(connection, null, $"PRAGMA user_version = {SchemaVersion}");
    }

    public void Create(LegacyRecord record, IReadOnlyList<EventEnvelope> events)
    {
        string name = record.Name?.Trim() ?? "";
        if (name.Length is < 1 or > MaxNameLength)
        {
            throw new InvalidLegacyNameException($"A legacy name is 1 to {MaxNameLength} characters.");
        }

        // The log is whole from the first event: 1, 2, 3, and so on up to the state's last sequence.
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Sequence != i + 1)
            {
                throw new ArgumentException($"Event {i + 1} has sequence {events[i].Sequence}; the events must run from 1 without a gap.", nameof(events));
            }
        }

        if (events.Count != record.State.NextSequence - 1)
        {
            throw new ArgumentException($"The events end at {events.Count}, and the state's next sequence is {record.State.NextSequence}.", nameof(events));
        }

        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        Execute(
            connection,
            transaction,
            "INSERT INTO legacy (legacy_id, seed, cycle, phase, week, next_sequence, rng_states, content_pack_id, content_pack_version, name, created_at) VALUES ($id, $seed, $cycle, $phase, $week, $next, $rng, $packId, $packVersion, $name, $createdAt)",
            StateParameters(record.State)
                .Append(("$packId", record.ContentPackId))
                .Append(("$packVersion", record.ContentPackVersion))
                .Append(("$name", name))
                .Append(("$createdAt", record.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)))
                .ToArray());
        WriteModules(connection, transaction, record.State);
        WriteEvents(connection, transaction, record.State.LegacyId, events);
        transaction.Commit();
    }

    public LegacyRecord? Load(Guid legacyId, IReadOnlyDictionary<string, Type> moduleTypes)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT seed, cycle, phase, week, next_sequence, rng_states, content_pack_id, content_pack_version, name, created_at FROM legacy WHERE legacy_id = $id";
        command.Parameters.AddWithValue("$id", legacyId.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var state = new LegacyState
        {
            LegacyId = legacyId,
            Seed = ulong.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            Cycle = reader.GetInt32(1),
            Phase = JsonSerializer.Deserialize<CyclePhase>(JsonSerializer.Serialize(reader.GetString(2)), SimJson.Options),
            Week = reader.GetInt32(3),
            NextSequence = reader.GetInt64(4),
            RngStates = JsonSerializer.Deserialize<Dictionary<string, ulong>>(reader.GetString(5), SimJson.Options)
                ?? throw new InvalidOperationException($"Legacy {legacyId} has no stored RNG states."),
        };
        var record = new LegacyRecord(state, reader.GetString(6), reader.GetInt32(7), reader.GetString(8), ParseTime(reader.GetString(9)));
        reader.Close();
        ReadModules(connection, state, moduleTypes);
        return record;
    }

    public void Commit(LegacyState state, IReadOnlyList<EventEnvelope> newEvents, long expectedSequence)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        int updated = Execute(
            connection,
            transaction,
            "UPDATE legacy SET cycle = $cycle, phase = $phase, week = $week, next_sequence = $next, rng_states = $rng WHERE legacy_id = $id AND seed = $seed AND next_sequence = $expected",
            StateParameters(state).Append(("$expected", expectedSequence)).ToArray());
        if (updated != 1)
        {
            // Nothing was written yet, so the transaction rolls back on dispose.
            long? found = StoredSequence(connection, transaction, state.LegacyId);
            throw found is null
                ? new InvalidOperationException($"Legacy {state.LegacyId} does not exist.")
                : new StaleLegacyException(state.LegacyId, expectedSequence, found.Value);
        }

        Execute(connection, transaction, "DELETE FROM module_state WHERE legacy_id = $id", ("$id", state.LegacyId.ToString()));
        WriteModules(connection, transaction, state);

        WriteEvents(connection, transaction, state.LegacyId, newEvents);

        transaction.Commit();
    }

    public IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId)
    {
        return ReadEvents(legacyId, "");
    }

    public IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId, int cycle)
    {
        return ReadEvents(legacyId, " AND cycle = $cycle", ("$cycle", cycle));
    }

    public IReadOnlyList<LegacyListing> ListLegacies()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT legacy_id, name, created_at, week, cycle FROM legacy ORDER BY created_at DESC, legacy_id";
        using SqliteDataReader reader = command.ExecuteReader();
        var listings = new List<LegacyListing>();
        while (reader.Read())
        {
            listings.Add(new LegacyListing(Guid.Parse(reader.GetString(0)), reader.GetString(1), ParseTime(reader.GetString(2)), reader.GetInt32(3), reader.GetInt32(4)));
        }

        return listings;
    }

    private IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId, string filter, params (string Name, object Value)[] parameters)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT sequence, cycle, type, version, payload FROM event_log WHERE legacy_id = $id{filter} ORDER BY sequence";
        command.Parameters.AddWithValue("$id", legacyId.ToString());
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using SqliteDataReader reader = command.ExecuteReader();
        var events = new List<EventEnvelope>();
        while (reader.Read())
        {
            events.Add(new EventEnvelope(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4)));
        }

        return events;
    }

    private static void WriteEvents(SqliteConnection connection, SqliteTransaction transaction, Guid legacyId, IReadOnlyList<EventEnvelope> events)
    {
        foreach (EventEnvelope e in events)
        {
            Execute(
                connection,
                transaction,
                "INSERT INTO event_log (legacy_id, sequence, cycle, type, version, payload) VALUES ($id, $sequence, $cycle, $type, $version, $payload)",
                ("$id", legacyId.ToString()),
                ("$sequence", e.Sequence),
                ("$cycle", e.Cycle),
                ("$type", e.Type),
                ("$version", e.Version),
                ("$payload", e.Payload));
        }
    }

    private static void WriteModules(SqliteConnection connection, SqliteTransaction transaction, LegacyState state)
    {
        foreach ((string module, object value) in state.Modules)
        {
            Execute(
                connection,
                transaction,
                "INSERT INTO module_state (legacy_id, module, json) VALUES ($id, $module, $json)",
                ("$id", state.LegacyId.ToString()),
                ("$module", module),
                ("$json", JsonSerializer.Serialize(value, value.GetType(), SimJson.Options)));
        }
    }

    private static void ReadModules(SqliteConnection connection, LegacyState state, IReadOnlyDictionary<string, Type> moduleTypes)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT module, json FROM module_state WHERE legacy_id = $id ORDER BY module";
        command.Parameters.AddWithValue("$id", state.LegacyId.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string module = reader.GetString(0);
            if (!moduleTypes.TryGetValue(module, out Type? type))
            {
                throw new InvalidOperationException($"Stored module '{module}' is not declared by any module.");
            }

            try
            {
                state.Modules[module] = JsonSerializer.Deserialize(reader.GetString(1), type, SimJson.Options)
                    ?? throw new JsonException("The stored state is null.");
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException($"Stored module '{module}' does not read as {type.Name}: {e.Message}", e);
            }
        }
    }

    private static DateTimeOffset ParseTime(string stored)
    {
        return DateTimeOffset.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static long? StoredSequence(SqliteConnection connection, SqliteTransaction transaction, Guid legacyId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT next_sequence FROM legacy WHERE legacy_id = $id";
        command.Parameters.AddWithValue("$id", legacyId.ToString());
        return (long?)command.ExecuteScalar();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static IEnumerable<(string, object)> StateParameters(LegacyState state)
    {
        return
        [
            ("$id", state.LegacyId.ToString()),
            ("$seed", state.Seed.ToString(CultureInfo.InvariantCulture)),
            ("$cycle", state.Cycle),
            ("$phase", state.Phase.ToString()),
            ("$week", state.Week),
            ("$next", state.NextSequence),
            ("$rng", JsonSerializer.Serialize(state.RngStates, SimJson.Options)),
        ];
    }

    private static int Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}

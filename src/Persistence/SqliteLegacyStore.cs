using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sim.Core;

namespace Persistence;

public sealed class SqliteLegacyStore : ILegacyStore
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS legacy (
            legacy_id TEXT PRIMARY KEY,
            seed TEXT NOT NULL,
            cycle INTEGER NOT NULL,
            phase TEXT NOT NULL,
            next_sequence INTEGER NOT NULL,
            rng_states TEXT NOT NULL,
            content_pack_id TEXT NOT NULL,
            content_pack_version INTEGER NOT NULL
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
        Execute(connection, null, Schema);
    }

    public void Create(LegacyRecord record)
    {
        using SqliteConnection connection = Open();
        Execute(
            connection,
            null,
            "INSERT INTO legacy VALUES ($id, $seed, $cycle, $phase, $next, $rng, $packId, $packVersion)",
            StateParameters(record.State).Append(("$packId", record.ContentPackId)).Append(("$packVersion", record.ContentPackVersion)).ToArray());
    }

    public LegacyRecord? Load(Guid legacyId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT seed, cycle, phase, next_sequence, rng_states, content_pack_id, content_pack_version FROM legacy WHERE legacy_id = $id";
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
            Phase = Enum.Parse<CyclePhase>(reader.GetString(2)),
            NextSequence = reader.GetInt64(3),
            RngStates = JsonSerializer.Deserialize<Dictionary<string, ulong>>(reader.GetString(4))!,
        };
        return new LegacyRecord(state, reader.GetString(5), reader.GetInt32(6));
    }

    public void Commit(LegacyState state, IReadOnlyList<EventEnvelope> newEvents)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        int updated = Execute(
            connection,
            transaction,
            "UPDATE legacy SET cycle = $cycle, phase = $phase, next_sequence = $next, rng_states = $rng WHERE legacy_id = $id AND seed = $seed",
            StateParameters(state).ToArray());
        if (updated != 1)
        {
            throw new InvalidOperationException($"Legacy {state.LegacyId} does not exist.");
        }

        foreach (EventEnvelope e in newEvents)
        {
            Execute(
                connection,
                transaction,
                "INSERT INTO event_log VALUES ($id, $sequence, $cycle, $type, $version, $payload)",
                ("$id", state.LegacyId.ToString()),
                ("$sequence", e.Sequence),
                ("$cycle", e.Cycle),
                ("$type", e.Type),
                ("$version", e.Version),
                ("$payload", e.Payload));
        }

        transaction.Commit();
    }

    public IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sequence, cycle, type, version, payload FROM event_log WHERE legacy_id = $id ORDER BY sequence";
        command.Parameters.AddWithValue("$id", legacyId.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        var events = new List<EventEnvelope>();
        while (reader.Read())
        {
            events.Add(new EventEnvelope(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4)));
        }

        return events;
    }

    private static IEnumerable<(string, object)> StateParameters(LegacyState state)
    {
        return
        [
            ("$id", state.LegacyId.ToString()),
            ("$seed", state.Seed.ToString(CultureInfo.InvariantCulture)),
            ("$cycle", state.Cycle),
            ("$phase", state.Phase.ToString()),
            ("$next", state.NextSequence),
            ("$rng", JsonSerializer.Serialize(state.RngStates)),
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

using System.Text.Json;
using Sim.Core;

namespace Persistence;

// Developer mode only: the way to keep a legacy safe from bugs while the game is under construction.
public static class LegacyExport
{
    private const int CurrentFormatVersion = 5;

    private static readonly JsonSerializerOptions Options = new(SimJson.Options) { WriteIndented = true };

    public static string Export(ILegacyStore store, Guid legacyId, IReadOnlyDictionary<string, Type> moduleTypes)
    {
        LegacyRecord record = store.Load(legacyId, moduleTypes) ?? throw new InvalidOperationException($"Legacy {legacyId} does not exist.");
        LegacyState state = record.State;
        var modules = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string name, object value) in state.Modules)
        {
            modules[name] = JsonSerializer.SerializeToElement(value, value.GetType(), SimJson.Options);
        }

        var legacy = new SnapshotLegacy(
            state.LegacyId,
            state.Seed,
            state.Cycle,
            state.Phase,
            state.Week,
            state.NextSequence,
            state.RngStates,
            modules,
            record.ContentPackId,
            record.ContentPackVersion,
            record.Name,
            record.CreatedAt);
        return JsonSerializer.Serialize(new Snapshot(CurrentFormatVersion, legacy, store.ReadEvents(legacyId)), Options);
    }

    // Fails if the legacy already exists; restore into an empty database or delete the legacy first.
    public static Guid Import(ILegacyStore store, string json, IReadOnlyDictionary<string, Type> moduleTypes)
    {
        Snapshot snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<Snapshot>(json, Options) ?? throw new InvalidOperationException("The snapshot is empty.");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"The snapshot does not read as a legacy export: {e.Message}", e);
        }

        if (snapshot.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidOperationException($"Unsupported snapshot format version {snapshot.FormatVersion}; expected {CurrentFormatVersion}.");
        }

        SnapshotLegacy legacy = snapshot.Legacy;
        var state = new LegacyState
        {
            LegacyId = legacy.LegacyId,
            Seed = legacy.Seed,
            Cycle = legacy.Cycle,
            Phase = legacy.Phase,
            Week = legacy.Week,
            NextSequence = legacy.NextSequence,
            RngStates = new Dictionary<string, ulong>(legacy.RngStates),
        };
        foreach ((string name, JsonElement value) in legacy.Modules)
        {
            if (!moduleTypes.TryGetValue(name, out Type? type))
            {
                throw new InvalidOperationException($"Snapshot module '{name}' is not declared by any module.");
            }

            try
            {
                state.Modules[name] = value.Deserialize(type, SimJson.Options) ?? throw new JsonException("The state is null.");
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException($"Snapshot module '{name}' does not read as {type.Name}: {e.Message}", e);
            }
        }

        try
        {
            store.Create(new LegacyRecord(state, legacy.ContentPackId, legacy.ContentPackVersion, legacy.Name, legacy.CreatedAt), snapshot.Events);
        }
        catch (InvalidLegacyNameException e)
        {
            throw new InvalidOperationException(e.Message, e);
        }

        return state.LegacyId;
    }

    private sealed record Snapshot(int FormatVersion, SnapshotLegacy Legacy, IReadOnlyList<EventEnvelope> Events);
}

internal sealed record SnapshotLegacy(
    Guid LegacyId,
    ulong Seed,
    int Cycle,
    CyclePhase Phase,
    int Week,
    long NextSequence,
    Dictionary<string, ulong> RngStates,
    SortedDictionary<string, JsonElement> Modules,
    string ContentPackId,
    int ContentPackVersion,
    string Name,
    DateTimeOffset CreatedAt);

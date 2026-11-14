using System.Text.Json;
using Sim.Core;

namespace Persistence;

// Developer mode only: the way to keep a legacy safe from bugs while the game is under construction.
public static class LegacyExport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Export(ILegacyStore store, Guid legacyId)
    {
        LegacyRecord record = store.Load(legacyId) ?? throw new InvalidOperationException($"Legacy {legacyId} does not exist.");
        return JsonSerializer.Serialize(new Snapshot(record, store.ReadEvents(legacyId)), Options);
    }

    // Fails if the legacy already exists; restore into an empty database or delete the legacy first.
    public static Guid Import(ILegacyStore store, string json)
    {
        Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(json) ?? throw new InvalidOperationException("The snapshot is empty.");
        store.Create(snapshot.Legacy);
        store.Commit(snapshot.Legacy.State, snapshot.Events);
        return snapshot.Legacy.State.LegacyId;
    }

    private sealed record Snapshot(LegacyRecord Legacy, IReadOnlyList<EventEnvelope> Events);
}

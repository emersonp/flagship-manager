using Sim.Core;

namespace Persistence;

public sealed record LegacyRecord(LegacyState State, string ContentPackId, int ContentPackVersion);

public interface ILegacyStore
{
    void Create(LegacyRecord record);

    LegacyRecord? Load(Guid legacyId);

    // Saves the state and appends its new events in one transaction.
    void Commit(LegacyState state, IReadOnlyList<EventEnvelope> newEvents);

    IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId);
}

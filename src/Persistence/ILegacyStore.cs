using Sim.Core;

namespace Persistence;

public sealed record LegacyRecord(LegacyState State, string ContentPackId, int ContentPackVersion);

public interface ILegacyStore
{
    void Create(LegacyRecord record);

    // Every stored module is read as the type declared for its name; an undeclared module is an error.
    LegacyRecord? Load(Guid legacyId, IReadOnlyDictionary<string, Type> moduleTypes);

    // Saves the state and appends its new events in one transaction.
    void Commit(LegacyState state, IReadOnlyList<EventEnvelope> newEvents);

    IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId);
}

using Sim.Core;

namespace Persistence;

public sealed record LegacyRecord(LegacyState State, string ContentPackId, int ContentPackVersion, string Name, DateTimeOffset CreatedAt);

public sealed record LegacyListing(Guid LegacyId, string Name, DateTimeOffset CreatedAt, int Week, int Cycle);

public sealed class StaleLegacyException(Guid legacyId, long expected, long found)
    : Exception($"Legacy {legacyId} moved on: expected sequence {expected}, found {found}.");

// A legacy name the store will not keep; the message is fit to show the player.
public sealed class InvalidLegacyNameException(string message) : Exception(message);

public interface ILegacyStore
{
    // One transaction: the legacy row, its module rows, and its events, or nothing.
    void Create(LegacyRecord record, IReadOnlyList<EventEnvelope> events);

    // Every stored module is read as the type declared for its name; an undeclared module is an error.
    LegacyRecord? Load(Guid legacyId, IReadOnlyDictionary<string, Type> moduleTypes);

    // Saves the state and appends its new events in one transaction.
    // Refuses with StaleLegacyException when the stored sequence is not expectedSequence.
    void Commit(LegacyState state, IReadOnlyList<EventEnvelope> newEvents, long expectedSequence);

    IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId);

    IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId, int cycle);

    // Newest first.
    IReadOnlyList<LegacyListing> ListLegacies();

    // One transaction: the legacy row, its module rows, and its events. False when there was no such legacy.
    bool Delete(Guid legacyId);
}

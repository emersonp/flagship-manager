using Persistence;
using Sim.Core;

namespace Web;

// One legacy as loaded for one request. Never shared between requests.
public sealed class LoadedLegacy
{
    private readonly GameServices services;
    private readonly List<EventEnvelope> envelopes = [];

    internal LoadedLegacy(LegacyRecord record, GameServices services)
    {
        Record = record;
        this.services = services;
    }

    public LegacyRecord Record { get; }

    public LegacyState State => Record.State;

    internal IReadOnlyList<EventEnvelope> Envelopes => envelopes;

    // Collects the envelopes for the commit.
    public void Send(ICommand command)
    {
        foreach (PublishedEvent published in services.Game.Bus.Send(State, command))
        {
            envelopes.Add(services.Game.Events.ToEnvelope(published));
        }
    }
}

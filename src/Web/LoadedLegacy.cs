using GameHost;
using Persistence;
using Sim.Core;

namespace Web;

// One legacy as loaded for one request. Never shared between requests.
public sealed class LoadedLegacy
{
    private readonly Game game;
    private readonly List<EventEnvelope> envelopes = [];

    internal LoadedLegacy(LegacyRecord record, Game game)
    {
        Record = record;
        this.game = game;
    }

    public LegacyRecord Record { get; }

    public LegacyState State => Record.State;

    internal IReadOnlyList<EventEnvelope> Envelopes => envelopes;

    // Collects the envelopes for the commit.
    public void Send(ICommand command)
    {
        foreach (PublishedEvent published in game.Bus.Send(State, command))
        {
            envelopes.Add(game.Events.ToEnvelope(published));
        }
    }
}

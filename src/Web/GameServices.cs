using GameHost;
using Persistence;
using Sim.Core.Content;

namespace Web;

// Built once at start-up: the loaded pack, the game, and the store.
public sealed class GameServices(ContentPack pack, Game game, ILegacyStore store, TimeProvider clock)
{
    public ContentPack Pack { get; } = pack;

    public Game Game { get; } = game;

    public ILegacyStore Store { get; } = store;

    public TimeProvider Clock { get; } = clock;
}

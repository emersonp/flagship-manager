using Persistence;

namespace Web;

// Built once at start-up: the store and the clock. The pack and the game are not here: they are swapped, so a request takes
// a snapshot from the ContentHost once and passes it down.
public sealed class GameServices(ILegacyStore store, TimeProvider clock)
{
    public ILegacyStore Store { get; } = store;

    public TimeProvider Clock { get; } = clock;
}

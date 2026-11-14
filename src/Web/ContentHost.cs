using GameHost;
using Sim.Core.Content;
using Web.Bridge;

namespace Web;

// The pack in play, the game built from it, and the names read from it: one object, replaced whole.
public sealed record LivePack(ContentPack Pack, Game Game, Names Names, int Generation);

// Holds the live pack. A request reads Current once and uses that snapshot throughout; a swap changes only later requests.
public sealed class ContentHost
{
    private readonly object gate = new();
    private LivePack? current;
    private IReadOnlyList<ContentError> loadErrors;

    public ContentHost(ContentPack? pack, IReadOnlyList<ContentError>? errors = null)
    {
        loadErrors = errors ?? [];
        if (pack is not null)
        {
            current = Prepare(pack) with { Generation = 1 };
        }
    }

    // Null only when developer mode started with no valid pack.
    public LivePack? Current => Volatile.Read(ref current);

    // What the loader said the last time it refused the files on disk; the page shown while no pack is live.
    public IReadOnlyList<ContentError> LoadErrors => Volatile.Read(ref loadErrors);

    public void Swap(ContentPack pack)
    {
        Install(Prepare(pack));
    }

    // Built before a write so that a pack the game cannot be built from stops the save, not the swap after it.
    public LivePack Prepare(ContentPack pack)
    {
        return new LivePack(pack, Game.Build(pack), new Names(pack), 0);
    }

    public void Install(LivePack live)
    {
        lock (gate)
        {
            // The generation is numbered here, so it only grows.
            Volatile.Write(ref current, live with { Generation = (current?.Generation ?? 0) + 1 });
            Volatile.Write(ref loadErrors, []);
        }
    }

    public void Refused(IReadOnlyList<ContentError> errors)
    {
        Volatile.Write(ref loadErrors, errors);
    }
}

using System.Diagnostics.CodeAnalysis;

namespace Sim.Core;

public enum CyclePhase
{
    Orders,
    Briefing,
    Resolution,
    Aftermath,
    Advance,
}

public sealed class LegacyState
{
    public required Guid LegacyId { get; init; }

    public required ulong Seed { get; init; }

    public int Cycle { get; set; } = 1;

    public CyclePhase Phase { get; set; } = CyclePhase.Orders;

    public int Week { get; set; } = 1;

    public long NextSequence { get; set; } = 1;

    // Keyed "cycle:streamName". Only the current cycle's streams are kept.
    public Dictionary<string, ulong> RngStates { get; init; } = new();

    // Keyed by module name. Each value is the module's declared state type.
    public Dictionary<string, object> Modules { get; init; } = new();

    // Set while guards and view factories run: reads then return a detached empty state instead of inserting one.
    internal bool ReadOnlyModules;

    // A read that never inserts: a legacy that has not been founded has no module states yet.
    public bool TryModule<TState>(string name, [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        if (!Modules.TryGetValue(name, out object? existing))
        {
            state = null;
            return false;
        }

        state = existing as TState
            ?? throw new InvalidOperationException($"Module '{name}' holds {existing.GetType().Name}, not {typeof(TState).Name}.");
        return true;
    }

    public TState Module<TState>(string name)
        where TState : class, new()
    {
        if (Modules.TryGetValue(name, out object? existing))
        {
            return existing as TState
                ?? throw new InvalidOperationException($"Module '{name}' holds {existing.GetType().Name}, not {typeof(TState).Name}.");
        }

        var created = new TState();
        if (!ReadOnlyModules)
        {
            Modules[name] = created;
        }

        return created;
    }
}

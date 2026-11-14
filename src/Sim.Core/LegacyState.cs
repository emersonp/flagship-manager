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

    public long NextSequence { get; set; } = 1;

    // Keyed "cycle:streamName". Only the current cycle's streams are kept.
    public Dictionary<string, ulong> RngStates { get; init; } = new();
}

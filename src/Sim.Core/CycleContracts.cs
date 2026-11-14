namespace Sim.Core;

public sealed record FoundLegacy : ICommand;

public sealed record AdvancePhase : ICommand;

[EventType("LegacyFounded", 1)]
public sealed record LegacyFounded : IEvent;

[EventType("PhaseEntered", 1)]
public sealed record PhaseEntered(int Cycle, CyclePhase Phase) : IEvent;

[EventType("CycleAdvanced", 1)]
public sealed record CycleAdvanced(int Cycle) : IEvent;

[EventType("WeekAdvanced", 1)]
public sealed record WeekAdvanced(int Week) : IEvent;

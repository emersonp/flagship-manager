namespace Sim.Core;

public sealed record AdvancePhase : ICommand;

[EventType("PhaseEntered", 1)]
public sealed record PhaseEntered(int Cycle, CyclePhase Phase) : IEvent;

[EventType("CycleAdvanced", 1)]
public sealed record CycleAdvanced(int Cycle) : IEvent;

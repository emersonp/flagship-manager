namespace Sim.Core;

public sealed record FoundLegacy : ICommand;

public sealed record AdvancePhase : ICommand;

// Turns the cycle as a trip of Weeks weeks would (Dev Play only, in the advance phase): one cycle, one roll times Weeks.
public sealed record Travel(int Weeks) : ICommand
{
    public const int Most = 10;
}

[EventType("LegacyFounded", 1)]
public sealed record LegacyFounded : IEvent;

[EventType("PhaseEntered", 1)]
public sealed record PhaseEntered(int Cycle, CyclePhase Phase) : IEvent;

[EventType("CycleAdvanced", 1)]
public sealed record CycleAdvanced(int Cycle) : IEvent;

// The cycle's length, rolled as the cycle turns: Days passed, against PlannedDays (one week, or N weeks for travel).
[EventType("CycleLengthRolled", 1)]
public sealed record CycleLengthRolled(int Days, int PlannedDays) : IEvent;

// Once for each day the calendar passes, in order; LegacyState.Day is already this day when its subscribers run.
[EventType("DayReached", 1)]
public sealed record DayReached(int Day) : IEvent;

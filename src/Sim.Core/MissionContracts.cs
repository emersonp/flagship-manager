namespace Sim.Core;

public enum MemberStatus
{
    Active,
    Incapacitated,
    Dead,
}

public enum MissionOutcome
{
    FullSuccess,
    PartialSuccess,
    Withdrawal,
    Loss,
    Incomplete,
}

public sealed record CrewResult(string Id, MemberStatus Status, int Injury, IReadOnlyDictionary<string, double> Experience, int Loyalty);

// A flag a mission changed. CrewId null: the ship.
public sealed record MissionFlag(string Flag, int Amount, bool Set, string? CrewId, string EncounterId);

// Flags: every flag change the mission made, in the order made.
public sealed record MissionResult(MissionOutcome Outcome, int Progress, int Need, int Calls, int Initiatives, IReadOnlyList<CrewResult> Crew, IReadOnlyList<MissionFlag> Flags);

[EventType("MissionStarted", 1)]
public sealed record MissionStarted(string MissionId, string ObjectiveId, string TemplateId, IReadOnlyList<string> CrewIds, string LeadId) : IEvent;

[EventType("MissionEnded", 1)]
public sealed record MissionEnded(string MissionId, string ObjectiveId, MissionResult Result) : IEvent;

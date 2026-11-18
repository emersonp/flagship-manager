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

// A trait a mission changed. CrewId null: the ship. TargetId: the crew member a pair trait points at (1e part three).
public sealed record MissionTrait(string Trait, int Amount, bool Set, string? CrewId, string EncounterId, string? TargetId = null);

// Traits: every trait change the mission made, in the order made.
public sealed record MissionResult(MissionOutcome Outcome, int Progress, int Need, int Calls, int Initiatives, IReadOnlyList<CrewResult> Crew, IReadOnlyList<MissionTrait> Traits);

[EventType("MissionStarted", 1)]
public sealed record MissionStarted(string MissionId, string ObjectiveId, string TemplateId, IReadOnlyList<string> CrewIds, string LeadId) : IEvent;

[EventType("MissionEnded", 1)]
public sealed record MissionEnded(string MissionId, string ObjectiveId, MissionResult Result) : IEvent;

// Published by the crew module; a contract here so other modules can let go of the dead.
[EventType("CrewDied", 1)]
public sealed record CrewDied(string CrewId, string MissionId) : IEvent;

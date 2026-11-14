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

public sealed record MissionResult(MissionOutcome Outcome, int Progress, int Need, int Calls, int Notices, IReadOnlyList<CrewResult> Crew);

[EventType("MissionStarted", 1)]
public sealed record MissionStarted(string MissionId, string ObjectiveId, string TemplateId, IReadOnlyList<string> CrewIds, string LeadId) : IEvent;

[EventType("MissionEnded", 1)]
public sealed record MissionEnded(string MissionId, string ObjectiveId, MissionResult Result) : IEvent;

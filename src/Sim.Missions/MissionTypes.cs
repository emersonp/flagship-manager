using Sim.Core.Content;

namespace Sim.Missions;

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
}

public enum Tier
{
    Critical,
    Success,
    Partial,
    Failure,
    Disaster,
}

public enum Approach
{
    Careful,
    Fast,
}

public enum CallAnswer
{
    Bold,
    Safe,
    Withdraw,
}

public enum InjuryOutcome
{
    Unharmed,
    LightInjury,
    Incapacitated,
    Dead,
}

// The resolver's whole view of a crew member. Caution and discipline run from -2 to +2.
public sealed record TeamMember(string Id, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline);

public sealed record Team(IReadOnlyList<TeamMember> Members, string LeadId);

// Operational parameters. Priority, risk, and autonomy run from -1 to +1.
public sealed record Parameters(int Priority = 0, int Risk = 0, int Autonomy = 0, bool DevelopJuniors = false);

public sealed record PendingCall(
    string DecisionId,
    int Stakes,
    double Threshold,
    string LeadId,
    CallAnswer LeadPrefers,
    IReadOnlyList<CallAnswer> Options,
    string Key);

public sealed record CrewResult(string Id, MemberStatus Status, int Injury, double Experience, int Loyalty);

public sealed record MissionResult(MissionOutcome Outcome, int Progress, int Need, int Calls, int Notices, IReadOnlyList<CrewResult> Crew);

public sealed record MissionStep(MissionState State, IReadOnlyList<Beat> Beats, PendingCall? Pending, MissionResult? Result);

public sealed record DrawnEncounter(string Slot, bool Skippable, EncounterDef Encounter);

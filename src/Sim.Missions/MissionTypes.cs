using Sim.Core;
using Sim.Core.Content;

namespace Sim.Missions;

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

// Operational parameters, each from -1 to +1. Risk is risk tolerance: +1 favours the fast approach.
public sealed record Parameters(int Priority = 0, int Risk = 0, int Autonomy = 0, bool DevelopJuniors = false);

public sealed record PendingCall(
    string DecisionId,
    int Stakes,
    double Threshold,
    string LeadId,
    CallAnswer LeadPrefers,
    IReadOnlyList<CallAnswer> Options,
    string Key);

public sealed record MissionStep(MissionState State, IReadOnlyList<Beat> Beats, PendingCall? Pending, MissionResult? Result);

public sealed record DrawnEncounter(string Slot, bool Skippable, EncounterDef Encounter);

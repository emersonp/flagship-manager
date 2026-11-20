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

// DiedAtEncounter and DiedAtApproach: where the injury roll that killed them fell (slice 1e part five). The approach is null for a
// collapse or a decision's roll, and both are null for the living.
public sealed record CrewResult(
    string Id,
    MemberStatus Status,
    int Injury,
    IReadOnlyDictionary<string, double> Experience,
    int Loyalty,
    string? DiedAtEncounter = null,
    string? DiedAtApproach = null);

// A trait a mission changed. CrewId null: the ship. TargetId: the crew member a pair trait points at (1e part three).
public sealed record MissionTrait(string Trait, int Amount, bool Set, string? CrewId, string EncounterId, string? TargetId = null);

// Traits: every trait change the mission made, in the order made.
public sealed record MissionResult(MissionOutcome Outcome, int Progress, int Need, int Calls, int Initiatives, IReadOnlyList<CrewResult> Crew, IReadOnlyList<MissionTrait> Traits);

[EventType("MissionStarted", 1)]
public sealed record MissionStarted(string MissionId, string ObjectiveId, string TemplateId, IReadOnlyList<string> CrewIds, string LeadId) : IEvent;

[EventType("MissionEnded", 1)]
public sealed record MissionEnded(string MissionId, string ObjectiveId, MissionResult Result) : IEvent;

// The captain's answer to a lead's call, published by the missions module where the answer falls among the beats (slice 1e part
// five), so the chronicle can record it without reading a mission beat. Answer is bold, safe, or withdraw; LeadPrefers bold or safe;
// Standing backed, overruled, or neutral: words, because CallAnswer and CallStanding are the missions module's own.
[EventType("CallAnswered", 1)]
public sealed record CallAnswered(string MissionId, string DecisionId, string EncounterId, string Answer, string LeadPrefers, string Standing, string LeadId) : IEvent;

// Published by the crew module; a contract here so other modules can let go of the dead. EncounterId and ApproachId: what killed
// them, from their CrewResult (version 2, slice 1e part five).
[EventType("CrewDied", 2)]
public sealed record CrewDied(string CrewId, string MissionId, string? EncounterId, string? ApproachId) : IEvent;

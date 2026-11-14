using System.Text.Json.Serialization;
using Sim.Core.Content;

namespace Sim.Missions;

// One recorded moment of a mission: the raw material for reports and the bench.
[JsonDerivedType(typeof(StageBeat), "stage")]
[JsonDerivedType(typeof(SkipBeat), "skip")]
[JsonDerivedType(typeof(CheckBeat), "check")]
[JsonDerivedType(typeof(EffectsBeat), "effects")]
[JsonDerivedType(typeof(InjuryBeat), "injury")]
[JsonDerivedType(typeof(LeadChangeBeat), "leadChange")]
[JsonDerivedType(typeof(ChainBeat), "chain")]
[JsonDerivedType(typeof(CallBeat), "call")]
[JsonDerivedType(typeof(NoticeBeat), "notice")]
[JsonDerivedType(typeof(AnswerBeat), "answer")]
[JsonDerivedType(typeof(CollapseBeat), "collapse")]
[JsonDerivedType(typeof(ResultBeat), "result")]
public abstract record Beat;

public sealed record StageBeat(int Cursor, string EncounterId, string Slot, bool CommsBlocked, int Momentum, int Strain) : Beat;

public sealed record SkipBeat(string EncounterId) : Beat;

public sealed record CheckBeat(
    string EncounterId,
    Approach Approach,
    string Skill,
    string ActorId,
    string? HelperId,
    bool Supervised,
    int ActorSkill,
    int Assist,
    int Gear,
    int Momentum,
    int Strain,
    int Score,
    int Roll,
    int Target,
    int Margin,
    Tier Tier,
    double Experience,
    bool Mentoring) : Beat;

public sealed record EffectsBeat(EffectDef Effects) : Beat;

public sealed record InjuryBeat(string CrewId, string EncounterId, InjurySeverity Severity, int Roll, InjuryOutcome Outcome) : Beat;

public sealed record LeadChangeBeat(string CrewId) : Beat;

public sealed record ChainBeat(string EncounterId) : Beat;

public sealed record CallBeat(PendingCall Call) : Beat;

public sealed record NoticeBeat(string DecisionId, int Stakes, double Threshold, string LeadId, CallAnswer Chose, bool Blocked) : Beat;

public sealed record AnswerBeat(CallAnswer Answer, bool Backed, string LeadId) : Beat;

public sealed record CollapseBeat : Beat;

public sealed record ResultBeat(MissionOutcome Outcome) : Beat;

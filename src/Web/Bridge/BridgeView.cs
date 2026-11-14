using Sim.Core;

namespace Web.Bridge;

// Everything a bridge screen is drawn from. Ids, numbers, and enum names only: no seeds, no stream states.
public sealed record BridgeView(TopBarView TopBar, IReadOnlyList<CrewRow> Roster, SiteView? Site, BriefingView? Briefing, MissionView? Mission, AftermathView? Aftermath);

public sealed record TopBarView(string LegacyName, int Week, int Cycle, CyclePhase Phase, string? PackWarning);

public sealed record CrewRow(string Id, int Seniority, string Status, bool Fit, IReadOnlyList<SkillCell> Skills, IReadOnlyList<int> LightInjuryHealWeeks, int? ReturnsAtWeek, int Loyalty, int AwayMissions);

public sealed record SkillCell(string SkillId, int Level, int EffectiveLevel);

public sealed record SiteView(string AssignmentId, IReadOnlyList<ObjectiveView> Objectives);

public sealed record ObjectiveView(string Id, string TemplateId, IReadOnlyList<string> PossibleSkills, int TeamSizeMin, int TeamSizeMax, int? UnattendedDifficulty, bool HasTeam);

public sealed record BriefingView(IReadOnlyList<ObjectiveView> Objectives, IReadOnlyList<CrewRow> Crew, IReadOnlyList<GearChoice> Gear, int LoadoutLimit);

public sealed record GearChoice(string Id, string Tag);

public sealed record MissionView(string MissionId, string ObjectiveId, IReadOnlyList<string> Team, string LeadId, int Momentum, int Strain, IReadOnlyList<LogLine> Log, CallView? Call, string? Outcome, IReadOnlyList<string> GearIds, int MomentumCap, int StrainCap);

public sealed record LogLine(int Index, string Kind, string Text);

// CallId is the resolver's string id; Options and LeadPrefers are CallAnswer names.
public sealed record CallView(string CallId, string DecisionId, int Stakes, IReadOnlyList<string> Options, string LeadPrefers, string LeadId);

public sealed record AftermathView(string? Outcome, IReadOnlyList<CrewChange> Changes, IReadOnlyList<UnattendedView> Unattended);

public sealed record CrewChange(string CrewId, string Text);

public sealed record UnattendedView(string ObjectiveId, string TemplateId, int Roll, int Target, bool Succeeded);

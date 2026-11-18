using Sim.Core;

namespace Web.Bridge;

// Everything a bridge screen is drawn from. Ids, numbers, and enum names only: no seeds, no stream states.
public sealed record BridgeView(TopBarView TopBar, IReadOnlyList<CrewRow> Roster, SiteView? Site, BriefingView? Briefing, MissionView? Mission, AftermathView? Aftermath, ItineraryView? Itinerary = null);

public sealed record TopBarView(string LegacyName, int Week, int Cycle, CyclePhase Phase, string? PackWarning);

public sealed record CrewRow(string Id, int Seniority, string Status, bool Fit, IReadOnlyList<SkillCell> Skills, IReadOnlyList<int> LightInjuryHealWeeks, int? ReturnsAtWeek, int Loyalty, int AwayMissions, bool IsAway = false, bool IsLead = false, string? Name = null, bool Disambiguate = false, string Morale = "Steady")
{
    // Disambiguate: another row in view shows the same name, so this one also shows its id.
    // The crew member's display name; the id stays the key.
    public string Shown => Name ?? Id;
}

public sealed record SkillCell(string SkillId, int Level, int EffectiveLevel, string? Name = null);

public sealed record SiteView(string AssignmentId, IReadOnlyList<ObjectiveView> Objectives, string? AssignmentName = null)
{
    public string AssignmentShown => AssignmentName ?? AssignmentId;
}

public sealed record ObjectiveView(string Id, string TemplateId, IReadOnlyList<string> PossibleSkills, int TeamSizeMin, int TeamSizeMax, int? UnattendedDifficulty, bool HasTeam, string? Name = null, IReadOnlyList<string>? PossibleSkillNames = null, bool Missing = false)
{
    // The template's name; Id is the objective's own id and TemplateId the template's.
    public string Shown => Name ?? TemplateId;

    public IReadOnlyList<string> SkillsShown => PossibleSkillNames ?? PossibleSkills;
}

public sealed record BriefingView(IReadOnlyList<ObjectiveView> Objectives, IReadOnlyList<CrewRow> Crew, IReadOnlyList<GearChoice> Gear, int LoadoutLimit);

public sealed record GearChoice(string Id, string Tag, string? Name = null, bool Disambiguate = false)
{
    public string Shown => Name ?? Id;
}

public sealed record MissionView(string MissionId, string ObjectiveId, IReadOnlyList<string> Team, string LeadId, int Momentum, int Strain, IReadOnlyList<LogLine> Log, CallView? Call, string? Outcome, IReadOnlyList<string> GearIds, int Cap, string StartLeadId, IReadOnlyList<string>? TeamNames = null, IReadOnlyList<string>? GearNames = null)
{
    // Names parallel to Team and GearIds; the ids stay the keys.
    public IReadOnlyList<string> TeamShown => TeamNames ?? Team;

    public IReadOnlyList<string> GearShown => GearNames ?? GearIds;
}

// One run of a line: a name (plain text, never scanned for figures) or the line's own text.
public readonly record struct LinePart(bool IsName, string Text);

// A stage line also carries its encounter, blocked flag, and figures as data; every other line has them null.
// Parts is the structural line cut into names and text; Text is always their join. Prose is what the captain reads: an authored
// line when Authored, otherwise the structural parts again. Tags are the figures the beat changed.
public sealed record LogLine(int Index, string Kind, string Text, string? EncounterId = null, bool? CommsBlocked = null, int? Momentum = null, int? Strain = null, IReadOnlyList<LinePart>? Parts = null, string? EncounterName = null, IReadOnlyList<LinePart>? Prose = null, IReadOnlyList<string>? Tags = null, bool Authored = false, IReadOnlyList<ProseEntry>? Script = null)
{
    // True when the authored prose is more than one sentence of narration, so it is shown as a script: speech, notes, and narration each on a line.
    public bool IsScript => Authored && Script is not null && Script is not [{ Kind: Sim.Core.Narrative.EntryKind.Narration }];

    public IReadOnlyList<LinePart> Segments => Parts ?? [new LinePart(false, Text)];
}

// CallId is the resolver's string id; Options and LeadPrefers are CallAnswer names. Labels is what each answer's button says, by
// answer word ("bold"); an answer with no label shows its name. What the team says on the call is its beat's script, in the log.
public sealed record CallView(string CallId, string DecisionId, int Stakes, IReadOnlyList<string> Options, string LeadPrefers, string LeadId, string? DecisionName = null, string? LeadName = null, IReadOnlyDictionary<string, string>? Labels = null)
{
    public string Label(string option) => Labels?.GetValueOrDefault(char.ToLowerInvariant(option[0]) + option[1..]) ?? option;

    public string DecisionShown => DecisionName ?? DecisionId;

    public string LeadShown => LeadName ?? LeadId;
}

// Report is null when no team was sent. Waiting and Settled are the storylets of "Matters aboard": those waiting for an answer in the
// order they arrived, and those answered this cycle in the order answered.
public sealed record AftermathView(string? Outcome, IReadOnlyList<CrewChange> Changes, IReadOnlyList<UnattendedView> Unattended, ReportView? Report = null);

// The captain's itinerary (1e part two): the storylets waiting and answered this cycle, the vignettes that arrived, and the
// captain's time, shown and not yet spent.
public sealed record ItineraryView(int CaptainsTime, IReadOnlyList<StoryletView> Waiting, IReadOnlyList<SettledStoryletView> Settled, IReadOnlyList<VignetteView> Vignettes)
{
    public bool IsEmpty => Waiting.Count == 0 && Settled.Count == 0 && Vignettes.Count == 0;
}

// A vignette that arrived this cycle. About names its subject and the fact that brought it ("CREW_03, lost a crewmate").
// Waiting: Options holds its two answers and DefaultLabel the one taken if it is left. Settled: Said is what the answer (or
// the default) said, and Tags what it changed; a vignette with no choice is settled as it arrives.
public sealed record VignetteView(string OfferId, string Title, string? About, IReadOnlyList<ProseEntry> Script, IReadOnlyList<StoryletOptionView> Options,
    string? DefaultLabel, bool Settled, IReadOnlyList<ProseEntry> Said, IReadOnlyList<string> Tags, bool Defaulted = false);

// A storylet waiting for an answer. From says where it came from ("From ENCOUNTER_01 · trait_a"), when an encounter set its trait.
public sealed record StoryletView(string OfferId, string Title, string? From, IReadOnlyList<ProseEntry> Script, IReadOnlyList<StoryletOptionView> Options);

public sealed record StoryletOptionView(string Id, string Label);

// A storylet answered this cycle: what the answer said, and tags for what it changed.
public sealed record SettledStoryletView(string Title, IReadOnlyList<ProseEntry> Said, IReadOnlyList<string> Tags);

// Kind is skill, injury, death, recovery, or loyalty.
public sealed record CrewChange(string CrewId, string Kind, string Text, IReadOnlyList<LinePart>? Parts = null)
{
    public IReadOnlyList<LinePart> Segments => Parts ?? [new LinePart(false, Text)];
}

public sealed record UnattendedView(string ObjectiveId, string TemplateId, int Roll, int Target, bool Succeeded, string? TemplateName = null)
{
    public string TemplateShown => TemplateName ?? TemplateId;
}

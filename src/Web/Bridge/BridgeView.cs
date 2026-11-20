using Sim.Core;

namespace Web.Bridge;

// Everything a bridge screen is drawn from. Ids, numbers, and enum names only: no seeds, no stream states.
public sealed record BridgeView(TopBarView TopBar, IReadOnlyList<CrewRow> Roster, SiteView? Site, BriefingView? Briefing, MissionView? Mission, AftermathView? Aftermath, ItineraryView? Itinerary = null);

// Dev: drawn for Dev Play, so ids and links to the content editor show; never set outside developer mode.
public sealed record TopBarView(string LegacyName, int Day, int Cycle, CyclePhase Phase, string? PackWarning, bool Dev = false);

// Traits (1e part three): a chip for each shown trait, for the roster's interim cell; the crew file (1e part five) replaces it.
public sealed record CrewRow(string Id, int Seniority, string Status, bool Fit, IReadOnlyList<SkillCell> Skills, IReadOnlyList<int> LightInjuryHealDays, int? ReturnsAtDay, int Loyalty, int AwayMissions, bool IsAway = false, bool IsLead = false, string? Name = null, bool Disambiguate = false, string Morale = "Steady", IReadOnlyList<string>? Traits = null)
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

// Chips and Launch (1e part three, design 4.7): what each picked crew member's traits say, and every launch rule the picks could fire;
// the page shows each whose condition its picks meet. Nothing here blocks a launch.
public sealed record BriefingView(IReadOnlyList<ObjectiveView> Objectives, IReadOnlyList<CrewRow> Crew, IReadOnlyList<GearChoice> Gear, int LoadoutLimit)
{
    public IReadOnlyList<BriefingChip> Chips { get; init; } = [];

    public IReadOnlyList<LaunchRow> Launch { get; init; } = [];

    // The last cycle's picks, made again where they still can be; null when the last cycle sent nobody.
    public BriefingPicks? Picks { get; init; }
}

// Crew: those still fit to go. Lead: null when the last lead is not among them. Gear: still in the content, up to the loadout limit.
public sealed record BriefingPicks(IReadOnlyList<string> Crew, string? Lead, IReadOnlyList<string> Gear, int Priority, int Autonomy);

// A trait's briefing line beside its holder, shown while they are picked (and, for a pair, while the target is too).
public sealed record BriefingChip(string HolderId, string? TargetId, string Text);

// One launch rule: When is one of LaunchWhen, read with the holder (and a pair's target) against the picks. Text: "CREW_01 sent,
// Stood down: Stood down to broken, Loyalty −2, Morale −4".
public sealed record LaunchRow(string HolderId, string? TargetId, string When, string Text);

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
public sealed record LogLine(int Index, string Kind, string Text, string? EncounterId = null, bool? CommsBlocked = null, int? Momentum = null, int? Strain = null, IReadOnlyList<LinePart>? Parts = null, string? EncounterName = null, IReadOnlyList<LinePart>? Prose = null, IReadOnlyList<string>? Tags = null, bool Authored = false, IReadOnlyList<ProseEntry>? Script = null, string? Source = null)
{
    // Source: the content editor's page for the line that said it, when an authored line did; shown only in Dev Play.

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
// captain's time, shown and not yet spent. Schedule: how the turn that began the cycle went (slice 1e-calendar); null for the first.
public sealed record ItineraryView(int CaptainsTime, IReadOnlyList<StoryletView> Waiting, IReadOnlyList<SettledStoryletView> Settled, IReadOnlyList<VignetteView> Vignettes,
    IReadOnlyList<ProseEntry>? Schedule = null)
{
    public bool IsEmpty => Waiting.Count == 0 && Settled.Count == 0 && Vignettes.Count == 0;
}

// A vignette that arrived this cycle. About names its cast and the fact that brought it ("CREW_03, lost a crewmate"; "CREW_01 · CREW_03").
// Waiting: Options holds its two answers and DefaultLabel the one taken if it is left. Settled: Said is what the answer (or
// the default) said, and Tags what it changed; a vignette with no choice is settled as it arrives.
public sealed record VignetteView(string OfferId, string Title, string? About, IReadOnlyList<ProseEntry> Script, IReadOnlyList<StoryletOptionView> Options,
    string? DefaultLabel, bool Settled, IReadOnlyList<ProseEntry> Said, IReadOnlyList<string> Tags, bool Defaulted = false);

// A storylet waiting for an answer. From says where it came from ("From ENCOUNTER_01 · trait_a"), when an encounter set its trait.
// About lists its cast by name in slot order (1e part four, Task 7): "Okafor · Vance"; null for a storylet for the ship.
public sealed record StoryletView(string OfferId, string Title, string? From, IReadOnlyList<ProseEntry> Script, IReadOnlyList<StoryletOptionView> Options, string? About = null);

// Handlers: for an option settled by a check (1e part four, Task 6), who may be sent, in roster order; null for any other. Empty: no
// one is fit to send, and the option is shown disabled with its reason (AnswerStorylet.NoOneFit).
public sealed record StoryletOptionView(string Id, string Label, IReadOnlyList<HandlerView>? Handlers = null);

// One crew member who may be sent: "CREW_01, SKILL_B 6", and the word for their chance ("likely").
public sealed record HandlerView(string CrewId, string Text, string Chance);

// A storylet answered this cycle: what the answer said, and tags for what it changed. Roll: for a check, how it went, as a check's
// line: "CREW_01 (SKILL_B 6, morale +1) rolled 7 against 11: success".
public sealed record SettledStoryletView(string Title, IReadOnlyList<ProseEntry> Said, IReadOnlyList<string> Tags, string? Roll = null);

// Kind is skill, injury, death, recovery, or loyalty.
public sealed record CrewChange(string CrewId, string Kind, string Text, IReadOnlyList<LinePart>? Parts = null)
{
    public IReadOnlyList<LinePart> Segments => Parts ?? [new LinePart(false, Text)];
}

public sealed record UnattendedView(string ObjectiveId, string TemplateId, int Roll, int Target, bool Succeeded, string? TemplateName = null)
{
    public string TemplateShown => TemplateName ?? TemplateId;
}

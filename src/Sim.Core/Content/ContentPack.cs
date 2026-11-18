namespace Sim.Core.Content;

public sealed record SkillDef(string Id, IReadOnlyList<string> Tags, string? Name = null);

public sealed record ContentPack(
    string Id,
    int Version,
    IReadOnlyList<SkillDef> Skills,
    IReadOnlyList<EncounterDef> Encounters,
    IReadOnlyList<ObjectiveDef> Objectives,
    MissionTuning Tuning,
    BenchTuning Bench,
    IReadOnlyList<CrewDef> Crew,
    IReadOnlyList<GearDef> Gear,
    IReadOnlyList<AssignmentDef> Assignments,
    CrewTuning CrewTuning)
{
    // Slice 1d. Optional: a pack with no lines shows the systems lines.
    public IReadOnlyList<LineDef> Lines { get; init; } = [];

    public IReadOnlyList<SymbolDef> Symbols { get; init; } = [];

    public IReadOnlyList<PronounSet> Pronouns { get; init; } = [];

    // Slice 1e, part three. Optional: a trait with no definition has no rules.
    public IReadOnlyList<TraitDef> Traits { get; init; } = [];

    // Slice 1e. Optional: a pack with no storylets offers none.
    public IReadOnlyList<StoryletDef> Storylets { get; init; } = [];

    // At most this many storylets wait in one itinerary (tuning storylets.perCycle).
    public int StoryletsPerCycle { get; init; } = 2;

    // Casting a storylet or vignette reads at most this many expressions before it gives up "out of tries" (tuning
    // storylets.castTries; 1e part four, design 4.4).
    public int CastTries { get; init; } = 100;

    // 1e part two. Optional: a pack with no vignettes sends none.
    public IReadOnlyList<VignetteDef> Vignettes { get; init; } = [];

    // At most this many vignettes arrive in one itinerary (tuning vignettes.perCycle).
    public int VignettesPerCycle { get; init; } = 3;

    // A vignette does not arrive again with the same cast within this many cycles (tuning vignettes.cooldown).
    public int VignetteCooldown { get; init; } = 6;

    // The history keeps each crew member's last this many cycles (tuning history.cycles; 1e part four, design 4.8).
    public int HistoryCycles { get; init; } = 20;

    // Shown on the itinerary and not yet spent (tuning captainsTime.perCycle; slice 1g spends it).
    public int CaptainsTimePerCycle { get; init; } = 3;

    // What a call's answer buttons say, by decision id and then by answer ("bold", "safe", "withdraw"); an answer with no label shows its own word.
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ChoiceLabels { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, string>>();

    // The crew member's pronoun set: the one named, else the pack's "they", else the built-in one.
    public PronounSet PronounsOf(CrewDef? crew)
    {
        string id = crew?.Pronouns ?? PronounSet.They.Id;
        return Pronouns.FirstOrDefault(p => p.Id == id) ?? PronounSet.They;
    }
}

public sealed record ContentError(string File, string Message);

public sealed record ContentLoadResult(ContentPack? Pack, IReadOnlyList<ContentError> Errors);

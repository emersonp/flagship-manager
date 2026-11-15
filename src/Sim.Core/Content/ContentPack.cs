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

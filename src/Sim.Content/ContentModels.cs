namespace Sim.Content;

public enum ContentKind
{
    Skill,
    Gear,
    Crew,
    Assignment,
    Objective,
    Encounter,
}

public sealed record ContentItemRef(ContentKind Kind, string Id, string? Name, string File);

// What every kind's model shares, so the writer and the index can treat them alike.
public interface IContentModel
{
    string Id { get; }

    string? Name { get; }
}

// Plain models the forms bind to. They mirror the YAML (every key the loader accepts), not the validated records.
public sealed record SkillModel(string Id, string? Name, IReadOnlyList<string> Tags) : IContentModel;

public sealed record GearModel(string Id, string? Name, string Tag) : IContentModel;

public sealed record CrewModel(string Id, string? Name, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline) : IContentModel;

public sealed record AssignmentModel(string Id, string? Name, IReadOnlyList<string> ObjectiveTags, IReadOnlyDictionary<int, int> Offer, int LoadoutLimit) : IContentModel;

public sealed record StageModel(string Slot, IReadOnlyList<string> DrawTags, int CountMin, int CountMax, bool Skippable);

public sealed record ObjectiveModel(
    string Id,
    string? Name,
    IReadOnlyList<string> Tags,
    int TeamSizeMin,
    int TeamSizeMax,
    int Need,
    int? UnattendedDifficulty,
    IReadOnlyList<StageModel> Stages) : IContentModel;

public sealed record ApproachModel(string Skill, int Difficulty, string? GearTag);

// One entry of a bold or safe list. The loader adds the entries together, so the list's order and split are kept as written.
public sealed record EffectModel(int? Progress, int? Momentum, int? Strain, string? InjuryRoll, bool Withdraw = false);

public sealed record DecisionModel(string Id, string? Name, int Stakes, IReadOnlyList<EffectModel> Bold, IReadOnlyList<EffectModel> Safe);

public sealed record EncounterModel(
    string Id,
    string? Name,
    IReadOnlyList<string> Tags,
    bool CommsBlocked,
    string? Chain,
    ApproachModel Careful,
    ApproachModel Fast,
    DecisionModel? Decision) : IContentModel;

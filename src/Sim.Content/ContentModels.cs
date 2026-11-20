namespace Sim.Content;

public enum ContentKind
{
    Skill,
    Gear,
    Crew,
    Assignment,
    Objective,
    Encounter,
    Line,
    Symbol,
    Pronoun,
}

// Lossy: this item has something the editor cannot show, so saving it replaces what it cannot show. A file-level problem (a top-level map,
// a second document) is not flagged here, only in ContentEdits.Losses, so a caller must check both.
public sealed record ContentItemRef(ContentKind Kind, string Id, string? Name, string File, bool Lossy = false);

// What every kind's model shares, so the writer and the index can treat them alike.
public interface IContentModel
{
    string Id { get; }

    string? Name { get; }
}

// Plain models the forms bind to. They mirror the YAML (every key the loader accepts), not the validated records.
public sealed record SkillModel(string Id, string? Name, IReadOnlyList<string> Tags) : IContentModel;

public sealed record GearModel(string Id, string? Name, string Tag) : IContentModel;

public sealed record CrewModel(string Id, string? Name, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline, string? Pronouns = null) : IContentModel;

public sealed record AssignmentModel(string Id, string? Name, IReadOnlyList<string> ObjectiveTags, IReadOnlyDictionary<int, int> Offer, int LoadoutLimit) : IContentModel;

public sealed record StageModel(string Slot, IReadOnlyList<string> DrawTags, int CountMin, int CountMax, bool Skippable);

// A line written inside an encounter or an objective: the same as a line of lines/, with no id, holding only for that item.
public sealed record OwnLineModel(string Beat, IReadOnlyList<CriterionModel> When, string Text);

// Text, here and on an encounter, an approach, and a decision, is the item's own words by key (NarrativeFacts lists the keys), as written.
public sealed record ObjectiveModel(
    string Id,
    string? Name,
    IReadOnlyList<string> Tags,
    int TeamSizeMin,
    int TeamSizeMax,
    int Need,
    int? UnattendedDifficulty,
    IReadOnlyList<StageModel> Stages,
    IReadOnlyDictionary<string, string>? Text = null,
    IReadOnlyList<OwnLineModel>? Lines = null) : IContentModel;

// A trait requirement (1e part three) has Trait and On, and Min and Max or Is, as written.
public sealed record RequirementModel(string? Skill, int? Level, string? Gear, string? Trait = null, int? Min = null, int? Max = null, string? Is = null, string? On = null);

public sealed record ApproachModel(
    string Id,
    string? Name,
    string Skill,
    int Difficulty,
    string? GearTag,
    IReadOnlyList<RequirementModel> Requires,
    IReadOnlyDictionary<string, IReadOnlyList<EffectModel>> Outcomes,
    IReadOnlyDictionary<string, string>? Text = null);

// One entry of a bold or safe list. The loader adds the entries together, so the list's order and split are kept as written.
// Trait is the entry's one trait change, if any: TraitAmount is what add or set says, TraitSet whether it is set, TraitOn what on says,
// and TraitToward what toward says (a pair, 1e part three).
public sealed record EffectModel(int? Progress, int? Momentum, int? Strain, string? InjuryRoll, bool Withdraw = false, string? Trait = null, int? TraitAmount = null, bool TraitSet = false, string? TraitOn = null, string? TraitToward = null);

// ChronicleWeight: chronicle: { weight: n } (slice 1e part five); null when the decision does not set it.
public sealed record DecisionModel(string Id, string? Name, int Stakes, IReadOnlyList<EffectModel> Bold, IReadOnlyList<EffectModel> Safe, IReadOnlyDictionary<string, string>? Text = null, IReadOnlyDictionary<string, string>? Labels = null, int? ChronicleWeight = null);

// One criterion as written: a value, a list of values, or a range. Values keep the text as written; IsList keeps [a] apart from a.
public sealed record CriterionModel(string Fact, IReadOnlyList<string> Values, bool IsList = false, int? Min = null, int? Max = null)
{
    public bool IsRange => Min is not null || Max is not null;
}

// Beat is written inside when, first.
public sealed record LineModel(string Id, string? Name, string Beat, IReadOnlyList<CriterionModel> When, string Text) : IContentModel;

public sealed record SymbolModel(string Id, string? Name, IReadOnlyList<string> Options) : IContentModel;

public sealed record PronounModel(string Id, string? Name, string Subject, string Object, string Possessive, string PossessivePronoun, string Reflexive, bool Plural) : IContentModel;

public sealed record EncounterModel(
    string Id,
    string? Name,
    IReadOnlyList<string> Tags,
    bool CommsBlocked,
    string? Chain,
    IReadOnlyList<ApproachModel> Approaches,
    DecisionModel? Decision,
    IReadOnlyDictionary<string, string>? Text = null,
    IReadOnlyList<OwnLineModel>? Lines = null) : IContentModel;

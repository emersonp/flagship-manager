namespace Sim.Core.Content;

public enum InjurySeverity
{
    Light,
    Severe,
}

// Exactly one of Skill and Gear is set. Skill with Level: someone standing has that level after injury. Gear: the loadout carries the tag.
public sealed record RequirementDef(string? Skill, int Level, string? Gear);

// One way to take an encounter. Id is unique within its encounter and is what beats and lines name.
public sealed record ApproachDef(string Id, string Skill, int Difficulty, string? GearTag,
    IReadOnlyList<RequirementDef> Requires, IReadOnlyDictionary<string, EffectDef> Outcomes, string? Name = null);

// One change to a trait. On is "ship", "actor", "lead", or "team" in a mission's effects. Set false means add.
public sealed record TraitChange(string Trait, int Amount, bool Set, string On);

// What a decision option does. Fields are additive, so several effects fold into one record. Traits is null, never empty, when there are
// none, so that two effects without traits still compare equal (a record compares a list by reference).
public sealed record EffectDef(int Momentum = 0, int Strain = 0, int Progress = 0, InjurySeverity? InjuryRoll = null, bool Withdraw = false, IReadOnlyList<TraitChange>? Traits = null);

public static class TraitOn
{
    // On is one of these in a mission's effects; actor only in an approach's outcome.
    public const string Ship = "ship";
    public const string Actor = "actor";
    public const string Lead = "lead";
    public const string Team = "team";

}

// Ids the game itself gives decisions, so no content may use them.
public static class DecisionIds
{
    // The call the resolver raises when a mission may be abandoned.
    public const string Abort = "DECISION_ABORT";
}

public sealed record DecisionDef(string Id, int Stakes, EffectDef Bold, EffectDef Safe, string? Name = null);

public sealed record EncounterDef(
    string Id,
    IReadOnlyList<string> Tags,
    bool CommsBlocked,
    string? Chain,
    IReadOnlyList<ApproachDef> Approaches,
    DecisionDef? Decision,
    string? Name = null)
{
    // An encounter has one to this many approaches.
    public const int MaxApproaches = 4;
}

public sealed record StageDef(string Slot, IReadOnlyList<string> DrawTags, int CountMin, int CountMax, bool Skippable);

public sealed record ObjectiveDef(
    string Id,
    IReadOnlyList<string> Tags,
    int TeamSizeMin,
    int TeamSizeMax,
    int Need,
    IReadOnlyList<StageDef> Stages,
    int? UnattendedDifficulty = null,
    string? Name = null);

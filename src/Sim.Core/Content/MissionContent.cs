namespace Sim.Core.Content;

public enum InjurySeverity
{
    Light,
    Severe,
}

public sealed record ApproachDef(string Skill, int Difficulty, string? GearTag);

// What a decision option does. Fields are additive, so several effects fold into one record.
public sealed record EffectDef(int Momentum = 0, int Strain = 0, int Progress = 0, InjurySeverity? InjuryRoll = null, bool Withdraw = false);

public sealed record DecisionDef(string Id, int Stakes, EffectDef Bold, EffectDef Safe, string? Name = null);

public sealed record EncounterDef(
    string Id,
    IReadOnlyList<string> Tags,
    bool CommsBlocked,
    string? Chain,
    ApproachDef Careful,
    ApproachDef Fast,
    DecisionDef? Decision,
    string? Name = null);

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

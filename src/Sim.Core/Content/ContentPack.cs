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
    CrewTuning CrewTuning);

public sealed record ContentError(string File, string Message);

public sealed record ContentLoadResult(ContentPack? Pack, IReadOnlyList<ContentError> Errors);

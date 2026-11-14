namespace Sim.Content;

public sealed record SkillDef(string Id, IReadOnlyList<string> Tags);

public sealed record ContentPack(string Id, int Version, IReadOnlyList<SkillDef> Skills);

public sealed record ContentError(string File, string Message);

public sealed record ContentLoadResult(ContentPack? Pack, IReadOnlyList<ContentError> Errors);

using System.Globalization;
using Sim.Core.Content;

namespace Sim.Content;

public static partial class ContentLoader
{
    // The id the resolver gives the abort call (MissionResolver), so no decision may use it.
    private const string AbortDecisionId = "DECISION_ABORT";

    private static List<EncounterDef> BuildEncounters(List<(string File, EncounterDto Dto)> items, List<SkillDef> skills, List<ContentError> errors)
    {
        var skillIds = skills.Select(s => s.Id).ToHashSet();
        var ids = items.Select(i => i.Dto.Id!).ToHashSet();
        var decisionOwners = new Dictionary<string, string>();
        var defs = new List<EncounterDef>();
        foreach ((string file, EncounterDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            ApproachDef? Approach(string name, ApproachDto? approach)
            {
                if (approach is null)
                {
                    Error($"The {name} approach is missing.");
                    return null;
                }

                if (approach.Skill is null || !skillIds.Contains(approach.Skill))
                {
                    Error($"The {name} approach uses unknown skill '{approach.Skill}'.");
                }

                if (approach.Difficulty < 1)
                {
                    Error($"The {name} approach's difficulty must be 1 or more.");
                }

                return new ApproachDef(approach.Skill ?? "", approach.Difficulty, approach.GearTag);
            }

            ApproachDef? careful = Approach("careful", dto.Approaches?.Careful);
            ApproachDef? fast = Approach("fast", dto.Approaches?.Fast);
            if (dto.Chain is not null && !ids.Contains(dto.Chain))
            {
                Error($"Chains to unknown encounter '{dto.Chain}'.");
            }

            if (dto.Chain is not null && dto.Chain == dto.Id)
            {
                errors.Add(new ContentError(file, $"{dto.Id} chains to itself."));
            }

            DecisionDef? decision = null;
            if (dto.Decision is not null)
            {
                if (dto.Decision.Id is null)
                {
                    Error("The decision has no id.");
                }

                if (dto.Decision.Stakes is < 1 or > 5)
                {
                    Error("The decision's stakes must be 1 to 5.");
                }

                if (dto.Decision.Id == AbortDecisionId)
                {
                    errors.Add(new ContentError(file, $"{dto.Id} decision id '{AbortDecisionId}' is reserved for the abort call."));
                }
                else if (dto.Decision.Id is not null && !decisionOwners.TryAdd(dto.Decision.Id, dto.Id!))
                {
                    errors.Add(new ContentError(file, $"Duplicate decision id '{dto.Decision.Id}', first used by {decisionOwners[dto.Decision.Id]}."));
                }

                string? decisionName = dto.Decision.Id is null ? null : CheckName(file, $"{dto.Id} decision {dto.Decision.Id}", dto.Decision.Name, errors);
                decision = new DecisionDef(dto.Decision.Id ?? "", dto.Decision.Stakes, Effects(dto.Decision.Bold, Error), Effects(dto.Decision.Safe, Error), decisionName);
            }

            string? name = CheckName(file, dto.Id!, dto.Name, errors);
            if (errors.Count == before)
            {
                defs.Add(new EncounterDef(dto.Id!, dto.Tags ?? [], dto.CommsBlocked, dto.Chain, careful!, fast!, decision, name));
            }
        }

        return defs;
    }

    private static EffectDef Effects(List<Dictionary<string, string>?>? items, Action<string> error)
    {
        var effect = new EffectDef();
        foreach ((string key, string value) in (items ?? []).SelectMany(i => i ?? []))
        {
            switch (key)
            {
                case "momentum" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n):
                    effect = effect with { Momentum = effect.Momentum + n };
                    break;
                case "strain" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n):
                    effect = effect with { Strain = effect.Strain + n };
                    break;
                case "progress" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n):
                    effect = effect with { Progress = effect.Progress + n };
                    break;
                case "injuryRoll" when value is "light" or "severe":
                    effect = effect with { InjuryRoll = value == "light" ? InjurySeverity.Light : InjurySeverity.Severe };
                    break;
                case "withdraw" when value == "true":
                    effect = effect with { Withdraw = true };
                    break;
                default:
                    error($"Unknown effect '{key}: {value}'.");
                    break;
            }
        }

        return effect;
    }

    private static List<ObjectiveDef> BuildObjectives(List<(string File, ObjectiveDto Dto)> items, List<EncounterDef> encounters, List<ContentError> errors)
    {
        var defs = new List<ObjectiveDef>();
        // The resolver never draws a chain target, so neither does this check.
        var chainTargets = encounters.Where(e => e.Chain is not null).Select(e => e.Chain!).ToHashSet();
        List<EncounterDef> drawable = encounters.Where(e => !chainTargets.Contains(e.Id)).ToList();
        foreach ((string file, ObjectiveDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            (int min, int max) = Range(dto.TeamSize, "teamSize", Error);
            if (dto.Need < 1)
            {
                Error("need must be 1 or more.");
            }

            if (dto.IfUnattended is { Difficulty: < 1 })
            {
                Error("ifUnattended.difficulty must be 1 or more.");
            }

            var stages = new List<StageDef>();
            foreach (StageDto? stage in dto.Stages ?? [])
            {
                if (stage?.Slot is null)
                {
                    Error("A stage has no slot.");
                    continue;
                }

                List<string> tags = stage.Draw?.Tags ?? [];
                (int countMin, int countMax) = Range(stage.Count, $"stage '{stage.Slot}' count", Error);
                int matches = drawable.Count(e => tags.All(e.Tags.Contains));
                if (matches < countMax)
                {
                    Error($"Stage '{stage.Slot}' draws up to {countMax} encounters tagged [{string.Join(", ", tags)}], but only {matches} match.");
                }

                stages.Add(new StageDef(stage.Slot, tags, countMin, countMax, stage.Skippable));
            }

            if (stages.Count == 0)
            {
                Error("An objective needs at least one stage.");
            }
            else if (errors.Count == before)
            {
                CheckWorstCase(stages, drawable, Error);
            }

            string? name = CheckName(file, dto.Id!, dto.Name, errors);
            if (errors.Count == before)
            {
                defs.Add(new ObjectiveDef(dto.Id!, dto.Tags ?? [], min, max, dto.Need, stages, dto.IfUnattended?.Difficulty, name));
            }
        }

        return defs;
    }

    // Stages draw without repeats from one pool, so every stage at its maximum count must still find enough.
    private static void CheckWorstCase(List<StageDef> stages, List<EncounterDef> drawable, Action<string> error)
    {
        var used = new HashSet<string>();
        foreach (StageDef stage in stages)
        {
            List<EncounterDef> open = drawable.Where(e => !used.Contains(e.Id) && stage.DrawTags.All(e.Tags.Contains)).ToList();
            if (open.Count < stage.CountMax)
            {
                error($"Stage '{stage.Slot}' may find only {open.Count} of the {stage.CountMax} encounters it draws, because earlier stages draw from the same pool.");
                return;
            }

            used.UnionWith(open.Take(stage.CountMax).Select(e => e.Id));
        }
    }

    // A count written as one number, or as [min, max].
    private static (int Min, int Max) Range(object? value, string name, Action<string> error)
    {
        List<object> parts = value switch
        {
            List<object> list => list,
            null => [],
            _ => [value, value],
        };
        if (parts.Count == 2
            && int.TryParse(parts[0]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min)
            && int.TryParse(parts[1]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max)
            && min >= 1
            && max >= min)
        {
            return (min, max);
        }

        error($"{name} must be a number of 1 or more, or [min, max].");
        return (1, 1);
    }

    private sealed class EncounterDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string>? Tags { get; set; }

        public bool CommsBlocked { get; set; }

        public string? Chain { get; set; }

        public ApproachesDto? Approaches { get; set; }

        public DecisionDto? Decision { get; set; }
    }

    private sealed class ApproachesDto
    {
        public ApproachDto? Careful { get; set; }

        public ApproachDto? Fast { get; set; }
    }

    private sealed class ApproachDto
    {
        public string? Skill { get; set; }

        public int Difficulty { get; set; }

        public string? GearTag { get; set; }
    }

    private sealed class DecisionDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public int Stakes { get; set; }

        public List<Dictionary<string, string>?>? Bold { get; set; }

        public List<Dictionary<string, string>?>? Safe { get; set; }
    }

    private sealed class ObjectiveDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string>? Tags { get; set; }

        public object? TeamSize { get; set; }

        public int Need { get; set; }

        public List<StageDto?>? Stages { get; set; }

        public UnattendedDto? IfUnattended { get; set; }
    }

    private sealed class UnattendedDto
    {
        public int Difficulty { get; set; }
    }

    private sealed class StageDto
    {
        public string? Slot { get; set; }

        public DrawDto? Draw { get; set; }

        public object? Count { get; set; }

        public bool Skippable { get; set; }
    }

    private sealed class DrawDto
    {
        public List<string>? Tags { get; set; }
    }
}

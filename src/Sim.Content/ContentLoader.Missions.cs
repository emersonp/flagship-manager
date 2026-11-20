using System.Globalization;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Content;

public static partial class ContentLoader
{
    private static List<EncounterDef> BuildEncounters(List<(string File, EncounterDto Dto)> items, List<SkillDef> skills, List<GearDef> gear, TraitBook book, List<ContentError> errors)
    {
        var skillIds = skills.Select(s => s.Id).ToHashSet();
        var gearTags = gear.Select(g => g.Tag).ToHashSet();
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

            var approaches = new List<ApproachDef>();
            var approachIds = new HashSet<string>();
            List<ApproachDto?> written = dto.Approaches ?? [];
            if (written.Count == 0)
            {
                Error("An encounter needs at least one approach.");
            }
            else if (written.Count > EncounterDef.MaxApproaches)
            {
                Error($"An encounter may have at most {EncounterDef.MaxApproaches} approaches.");
            }

            for (int i = 0; i < written.Count; i++)
            {
                if (written[i] is not { } approach)
                {
                    Error($"approaches[{i + 1}] is blank.");
                    continue;
                }

                string label = approach.Id ?? $"approaches[{i + 1}]";
                if (approach.Id is null || !Ids.IsInnerId(approach.Id))
                {
                    Error($"Approach id '{approach.Id}' must be {Said(Ids.InnerRule)}");
                }
                else if (!approachIds.Add(approach.Id))
                {
                    Error($"Approach id '{approach.Id}' is used twice.");
                }

                if (approach.Skill is null || !skillIds.Contains(approach.Skill))
                {
                    Error($"The {label} approach uses unknown skill '{approach.Skill}'.");
                }

                if (approach.Difficulty < 1)
                {
                    Error($"The {label} approach's difficulty must be 1 or more.");
                }

                string? approachName = CheckName(file, $"{dto.Id} approach {label}", approach.Name, errors);
                List<RequirementDef> requires = Requirements(label, approach.Requires, skillIds, gearTags, book, Error);
                Dictionary<string, EffectDef> outcomes = Outcomes(label, approach.Outcomes, book, Error);
                approaches.Add(new ApproachDef(approach.Id ?? "", approach.Skill ?? "", approach.Difficulty, approach.GearTag, requires, outcomes, approachName));
            }

            // So the lead always has an approach to take, whatever team is sent.
            if (approaches.Count > 0 && approaches.All(a => a.Requires.Count > 0))
            {
                Error("At least one approach must have no requirements.");
            }

            if (dto.Chain is not null && !ids.Contains(dto.Chain))
            {
                Error($"Chains to unknown encounter '{dto.Chain}'.");
            }

            if (dto.Chain is not null && dto.Chain == dto.Id)
            {
                Error("chains to itself.");
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

                if (dto.Decision.Id == DecisionIds.Abort)
                {
                    Error($"decision id '{DecisionIds.Abort}' is reserved for the abort call.");
                }
                else if (dto.Decision.Id is not null && !decisionOwners.TryAdd(dto.Decision.Id, dto.Id!))
                {
                    Error($"decision id '{dto.Decision.Id}' is also used by {decisionOwners[dto.Decision.Id]}.");
                }

                int? chronicleWeight = null;
                if (dto.Decision.Chronicle is { } chronicle)
                {
                    if (chronicle.Text is not null)
                    {
                        Error("The decision's chronicle has text, which only a storylet option may have.");
                    }

                    if (chronicle.Weight is not { } weight || weight < 0 || weight > Sim.Core.ChronicleTuning.Most)
                    {
                        Error($"The decision's chronicle weight must be a whole number from 0 to {Sim.Core.ChronicleTuning.Most}.");
                    }
                    else
                    {
                        chronicleWeight = weight;
                    }
                }

                string? decisionName = dto.Decision.Id is null ? null : CheckName(file, $"{dto.Id} decision {dto.Decision.Id}", dto.Decision.Name, errors);
                decision = new DecisionDef(dto.Decision.Id ?? "", dto.Decision.Stakes, Effects(dto.Decision.Bold, book, Error), Effects(dto.Decision.Safe, book, Error), decisionName, chronicleWeight);
            }

            CheckTags(dto.Tags, Error);
            string? name = CheckName(file, dto.Id!, dto.Name, errors);
            if (errors.Count == before)
            {
                defs.Add(new EncounterDef(dto.Id!, dto.Tags ?? [], dto.CommsBlocked, dto.Chain, approaches, decision, name));
            }
        }

        return defs;
    }

    // Any number of requirements; each names exactly one of a skill (with a level of 1 or more), a gear tag some gear carries, and a
    // trait on the ship, the lead, or anyone standing (1e part three, design 4.6).
    private static List<RequirementDef> Requirements(string label, List<RequirementDto?>? items, HashSet<string> skillIds, HashSet<string> gearTags, TraitBook book, Action<string> error)
    {
        var requires = new List<RequirementDef>();
        for (int j = 0; j < (items?.Count ?? 0); j++)
        {
            RequirementDto requirement = items![j] ?? new RequirementDto();
            if (new[] { requirement.Skill, requirement.Gear, requirement.Trait }.Count(n => n is not null) != 1)
            {
                error($"The {label} approach's requirement {j + 1} must name one of skill, gear, and trait.");
                continue;
            }

            if (requirement.Trait is { } trait)
            {
                if (TraitRequirement(label, j, requirement, trait, book, error) is { } def)
                {
                    requires.Add(def);
                }

                continue;
            }

            if (requirement.Min is not null || requirement.Max is not null || requirement.Is is not null || requirement.On is not null)
            {
                error($"The {label} approach's requirement {j + 1}: min, max, is, and on belong to a trait.");
                continue;
            }

            if (requirement.Skill is { } skill)
            {
                if (!skillIds.Contains(skill))
                {
                    error($"The {label} approach requires unknown skill '{skill}'.");
                }

                if (requirement.Level < 1)
                {
                    error($"The {label} approach's requirement {j + 1} needs a level of 1 or more.");
                }
            }

            // No team could ever meet it, and the form could not show it to be fixed.
            if (requirement.Gear is { } tag && !gearTags.Contains(tag))
            {
                error($"Requires gear tagged '{tag}', which no gear carries.");
            }

            requires.Add(new RequirementDef(requirement.Skill, requirement.Level, requirement.Gear));
        }

        return requires;
    }

    // { trait, min, max, is, on }: min is 1 when left out, or 0 when only max is given, as on a storylet. A pair trait cannot be
    // required here.
    private static RequirementDef? TraitRequirement(string label, int j, RequirementDto requirement, string trait, TraitBook book, Action<string> error)
    {
        string where = $"The {label} approach's requirement {j + 1}";
        if (!Ids.IsInnerId(trait))
        {
            error($"Trait id '{trait}' must be {Said(Ids.InnerRule)}");
            return null;
        }

        if (requirement.On is not (TraitOn.Ship or TraitOn.Lead or TraitOn.Anyone))
        {
            error($"{where} needs on: ship, lead, or anyone.");
            return null;
        }

        if (requirement.Level != 0)
        {
            error($"{where}: a trait takes min, max, or is, not level.");
            return null;
        }

        int min = requirement.Min ?? (requirement.Max is not null ? 0 : 1);
        int? max = requirement.Max;
        if (requirement.Is is { } exact)
        {
            if (requirement.Min is not null || requirement.Max is not null)
            {
                error("A requirement has is, or min and max, not both.");
                return null;
            }

            if (book.Is(trait, exact, error) is not { } value)
            {
                return null;
            }

            (min, max) = (value, value);
        }

        if (min < 0 || max < 0)
        {
            error("A requirement's min and max must be whole numbers of 0 or more.");
            return null;
        }

        if (max < min)
        {
            error($"The requirement on {trait} has max {max} below min {min}.");
            return null;
        }

        return book.CheckPlace(trait, requirement.On == TraitOn.Ship ? TraitPlaces.Ship : TraitPlaces.Crew, error)
            ? new RequirementDef(null, 0, null, trait, min, max, requirement.On)
            : null;
    }

    // An outcome written under a tier word replaces that tier's default whole; an empty list is an outcome that does nothing.
    private static Dictionary<string, EffectDef> Outcomes(string label, Dictionary<string, List<Dictionary<string, string>?>?>? items, TraitBook book, Action<string> error)
    {
        var outcomes = new Dictionary<string, EffectDef>();
        foreach ((string tier, List<Dictionary<string, string>?>? effects) in items ?? [])
        {
            if (!NarrativeFacts.Tiers.Contains(tier))
            {
                error($"The {label} approach has an outcome for '{tier}', which is not a tier: {string.Join(", ", NarrativeFacts.Tiers.Take(NarrativeFacts.Tiers.Count - 1))}, or {NarrativeFacts.Tiers[^1]}.");
                continue;
            }

            EffectDef effect = Effects(effects, book, error, outcome: true);
            if (effect.Withdraw)
            {
                error("An outcome cannot withdraw the team.");
            }

            outcomes[tier] = effect;
        }

        return outcomes;
    }

    // The entries fold into one effect. An entry that names a trait is one trait change, and its add or set and its on belong to it.
    // outcome: the effect is an approach's outcome, where on: actor has someone to name.
    private static EffectDef Effects(List<Dictionary<string, string>?>? items, TraitBook book, Action<string> error, bool outcome = false)
    {
        var effect = new EffectDef();
        var traits = new List<TraitChange>();
        foreach (Dictionary<string, string> entry in (items ?? []).Select(i => i ?? []))
        {
            bool named = entry.TryGetValue("trait", out string? trait);
            if (named && TraitChangeOf(entry, trait!, outcome, book, error) is { } change)
            {
                traits.Add(change);
            }

            foreach ((string key, string value) in entry)
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
                    case "trait" or "add" or "set" or "on" or "toward" when named:
                        break;
                    case "flag":
                        error(FlagIsNowTrait);
                        break;
                    default:
                        error($"Unknown effect '{key}: {value}'.");
                        break;
                }
            }
        }

        // Null, not empty, when there are none, so effects without traits compare equal.
        return traits.Count == 0 ? effect : effect with { Traits = traits };
    }

    // One trait change, or null when it is refused (each problem reported). A set may name one of the trait's levels; a trait with
    // levels is never added to; and the definition says whether it may sit on the ship or on crew (design 4.3).
    private static TraitChange? TraitChangeOf(Dictionary<string, string> entry, string trait, bool outcome, TraitBook book, Action<string> error)
    {
        bool ok = true;
        void Refuse(string message)
        {
            error(message);
            ok = false;
        }

        if (!Ids.IsInnerId(trait))
        {
            Refuse($"Trait id '{trait}' must be {Said(Ids.InnerRule)}");
        }

        bool set = entry.TryGetValue("set", out string? setText);
        bool add = entry.TryGetValue("add", out string? addText);
        int amount = 0;
        if (set == add)
        {
            Refuse("A trait change needs add or set, not both.");
        }
        else if (book.Def(trait) is { HasLevels: true } levelled)
        {
            if (!set)
            {
                Refuse(levelled.LevelsAreSet);
            }
            else if (book.SetValue(trait, setText!, Refuse) is { } value)
            {
                amount = value;
            }
        }
        else if (!int.TryParse(set ? setText : addText, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
        {
            Refuse($"Unknown effect '{(set ? "set" : "add")}: {(set ? setText : addText)}'.");
        }
        else if (set && amount < 0)
        {
            Refuse("A trait's set must be 0 or more.");
        }

        string? on = entry.GetValueOrDefault("on");
        if (on is not (TraitOn.Ship or TraitOn.Actor or TraitOn.Lead or TraitOn.Team))
        {
            Refuse("A trait change needs on: ship, actor, lead, or team.");
        }
        else if (on == TraitOn.Actor && !outcome)
        {
            Refuse("on: actor is only for an approach's outcome.");
        }

        string? toward = entry.GetValueOrDefault("toward");
        if (toward is not null)
        {
            // A pair (1e part three, design 4.5): the actor toward the lead, or the lead toward the actor.
            if (on is not (TraitOn.Actor or TraitOn.Lead) || toward is not (TraitOn.Actor or TraitOn.Lead) || toward == on)
            {
                Refuse("toward names lead or actor, with on: actor or on: lead, and never the same word as on.");
            }
            else if (!outcome)
            {
                Refuse("toward is only for an approach's outcome, where there is an actor.");
            }
        }

        if (ok && Ids.IsInnerId(trait))
        {
            ok &= book.CheckPlace(trait, toward is not null ? TraitPlaces.Pair : on == TraitOn.Ship ? TraitPlaces.Ship : TraitPlaces.Crew, error);
        }

        return ok ? new TraitChange(trait, amount, set, on!, toward) : null;
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
                CheckTags(tags, Error);
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

            CheckTags(dto.Tags, Error);
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

        public List<ApproachDto?>? Approaches { get; set; }

        public DecisionDto? Decision { get; set; }

        public Dictionary<string, string?>? Text { get; set; }

        public List<LineDto?>? Lines { get; set; }
    }

    private sealed class ApproachDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Skill { get; set; }

        public int Difficulty { get; set; }

        public string? GearTag { get; set; }

        public List<RequirementDto?>? Requires { get; set; }

        public Dictionary<string, List<Dictionary<string, string>?>?>? Outcomes { get; set; }

        public Dictionary<string, string?>? Text { get; set; }
    }

    private sealed class RequirementDto
    {
        public string? Skill { get; set; }

        public int Level { get; set; }

        public string? Gear { get; set; }

        // A trait's (1e part three).
        public string? Trait { get; set; }

        public int? Min { get; set; }

        public int? Max { get; set; }

        public string? Is { get; set; }

        public string? On { get; set; }
    }

    private sealed class DecisionDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public int Stakes { get; set; }

        public List<Dictionary<string, string>?>? Bold { get; set; }

        public List<Dictionary<string, string>?>? Safe { get; set; }

        public Dictionary<string, string?>? Text { get; set; }

        public Dictionary<string, string?>? Labels { get; set; }

        public ChronicleDto? Chronicle { get; set; }
    }

    // chronicle: on a decision (slice 1e part five, design 4.4). Text is read only to be refused: it belongs on a storylet option.
    private sealed class ChronicleDto
    {
        public int? Weight { get; set; }

        public string? Text { get; set; }
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

        public Dictionary<string, string?>? Text { get; set; }

        public List<LineDto?>? Lines { get; set; }
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

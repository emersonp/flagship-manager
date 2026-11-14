using Sim.Core.Content;
using YamlDotNet.Serialization;

namespace Sim.Content;

public static partial class ContentLoader
{
    private static CrewTuning? LoadCrewTuning(IDeserializer yaml, IReadOnlyDictionary<string, string> files, List<ContentError> errors)
    {
        if (!files.TryGetValue(CrewTuningFile, out string? text))
        {
            errors.Add(new ContentError(CrewTuningFile, $"The pack has no {CrewTuningFile}."));
            return null;
        }

        Dictionary<object, object>? root = Parse<Dictionary<object, object>>(yaml, CrewTuningFile, text, errors);
        if (root is null)
        {
            return null;
        }

        var values = new Dictionary<string, string>();
        Flatten("", root, values);
        var read = new TuningReader(CrewTuningFile, values, errors);
        int errorsBefore = errors.Count;

        var tuning = new CrewTuning
        {
            XpBase = read.Int("growth.xpBase", min: 1),
            MaxLevel = read.Int("growth.maxLevel", min: 1),
            LightInjuryWeeks = read.Int("injury.lightWeeks", min: 1),
            IncapacitatedWeeks = read.Int("injury.incapacitatedWeeks", min: 1),
            StartingLoyalty = read.Int("loyalty.starting"),
            LoyaltyPerPoint = read.Int("loyalty.perPoint"),
            LoyaltyMin = read.Int("loyalty.min"),
            LoyaltyMax = read.Int("loyalty.max"),
        };

        if (read.Complete)
        {
            if (tuning.LoyaltyMin > tuning.StartingLoyalty)
            {
                errors.Add(new ContentError(CrewTuningFile, "loyalty.min must not be above loyalty.starting."));
            }

            if (tuning.StartingLoyalty > tuning.LoyaltyMax)
            {
                errors.Add(new ContentError(CrewTuningFile, "loyalty.starting must not be above loyalty.max."));
            }
        }

        return read.ReportUnused() && errors.Count == errorsBefore ? tuning : null;
    }

    private static List<CrewDef> BuildCrew(List<(string File, CrewDto Dto)> items, List<SkillDef> skills, CrewTuning? tuning, List<ContentError> errors)
    {
        var skillIds = skills.Select(s => s.Id).ToHashSet();
        var defs = new List<CrewDef>();
        foreach ((string file, CrewDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            if (dto.Seniority < 1)
            {
                Error("seniority must be 1 or more.");
            }

            foreach ((string skill, int level) in dto.Skills ?? [])
            {
                if (!skillIds.Contains(skill))
                {
                    Error($"unknown skill '{skill}'.");
                }
                else if (tuning is not null && (level < 0 || level > tuning.MaxLevel))
                {
                    Error($"skill '{skill}' must be 0 to {tuning.MaxLevel}, not {level}.");
                }
            }

            if (dto.Caution is < -2 or > 2)
            {
                Error($"caution must be -2 to 2, not {dto.Caution}.");
            }

            if (dto.Discipline is < -2 or > 2)
            {
                Error($"discipline must be -2 to 2, not {dto.Discipline}.");
            }

            if (errors.Count == before)
            {
                defs.Add(new CrewDef(dto.Id!, dto.Seniority, dto.Skills ?? [], dto.Caution, dto.Discipline));
            }
        }

        if (items.Count == 0)
        {
            errors.Add(new ContentError("crew/starting.yaml", "The pack has no crew."));
        }

        return defs;
    }

    private static List<GearDef> BuildGear(List<(string File, GearDto Dto)> items, List<ContentError> errors)
    {
        var defs = new List<GearDef>();
        foreach ((string file, GearDto dto) in items)
        {
            if (string.IsNullOrWhiteSpace(dto.Tag))
            {
                errors.Add(new ContentError(file, $"{dto.Id}: tag must not be empty."));
                continue;
            }

            defs.Add(new GearDef(dto.Id!, dto.Tag));
        }

        return defs;
    }

    private static List<AssignmentDef> BuildAssignments(List<(string File, AssignmentDto Dto)> items, List<ObjectiveDto> objectives, List<ContentError> errors)
    {
        var defs = new List<AssignmentDef>();
        foreach ((string file, AssignmentDto dto) in items)
        {
            int before = errors.Count;
            void Error(string message)
            {
                errors.Add(new ContentError(file, $"{dto.Id}: {message}"));
            }

            List<string> tags = dto.Objectives?.Tags ?? [];
            Dictionary<int, int> offer = dto.Offer ?? [];
            if (offer.Count == 0)
            {
                Error("offer must list at least one count.");
            }

            foreach ((int count, int weight) in offer.OrderBy(o => o.Key))
            {
                if (count < 1)
                {
                    Error($"offer count {count} must be 1 or more.");
                }

                if (weight < 1)
                {
                    Error($"offer weight for {count} must be 1 or more, not {weight}.");
                }
            }

            // Counted over every parsed objective, so one broken objective does not also fail its assignments.
            int matches = objectives.Count(o => tags.All((o.Tags ?? []).Contains));
            if (offer.Count > 0 && offer.Keys.Max() > matches)
            {
                Error($"offers up to {offer.Keys.Max()} objectives, but the objective templates matching tags [{string.Join(", ", tags)}] number only {matches}.");
            }

            if (dto.LoadoutLimit < 0)
            {
                Error("loadoutLimit must be 0 or more.");
            }

            if (errors.Count == before)
            {
                defs.Add(new AssignmentDef(dto.Id!, tags, offer, dto.LoadoutLimit));
            }
        }

        if (items.Count == 0)
        {
            errors.Add(new ContentError("assignments/base.yaml", "The pack has no assignment."));
        }

        return defs;
    }

    private sealed class CrewDto
    {
        public string? Id { get; set; }

        public int Seniority { get; set; }

        public Dictionary<string, int>? Skills { get; set; }

        public int Caution { get; set; }

        public int Discipline { get; set; }
    }

    private sealed class GearDto
    {
        public string? Id { get; set; }

        public string? Tag { get; set; }
    }

    private sealed class AssignmentDto
    {
        public string? Id { get; set; }

        public AssignmentObjectivesDto? Objectives { get; set; }

        public Dictionary<int, int>? Offer { get; set; }

        public int LoadoutLimit { get; set; }
    }

    private sealed class AssignmentObjectivesDto
    {
        public List<string>? Tags { get; set; }
    }
}

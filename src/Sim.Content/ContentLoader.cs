using System.Text.RegularExpressions;
using Sim.Core.Content;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sim.Content;

public static partial class ContentLoader
{
    private const string ManifestFile = "pack.yaml";
    private const string TuningFile = "tuning/missions.yaml";
    private const string CrewTuningFile = "tuning/crew.yaml";

    // Keys are paths relative to the pack root, with forward slashes.
    public static ContentLoadResult Load(IReadOnlyDictionary<string, string> files)
    {
        var errors = new List<ContentError>();
        IDeserializer yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).WithDuplicateKeyChecking().Build();

        PackDto? manifest = null;
        if (files.TryGetValue(ManifestFile, out string? manifestText))
        {
            manifest = Parse<PackDto>(yaml, ManifestFile, manifestText, errors);
            if (manifest is not null)
            {
                CheckId(ManifestFile, manifest.Id, errors);
                if (manifest.Version < 1)
                {
                    errors.Add(new ContentError(ManifestFile, "version must be 1 or more."));
                }
            }
        }
        else
        {
            errors.Add(new ContentError(ManifestFile, "The pack has no pack.yaml."));
        }

        var skills = new List<SkillDef>();
        var encounters = new List<(string File, EncounterDto Dto)>();
        var objectives = new List<(string File, ObjectiveDto Dto)>();
        var crew = new List<(string File, CrewDto Dto)>();
        var gear = new List<(string File, GearDto Dto)>();
        var assignments = new List<(string File, AssignmentDto Dto)>();
        var seenIn = new Dictionary<string, string>();
        foreach ((string path, string text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (path == ManifestFile || path == TuningFile || path == CrewTuningFile)
            {
                continue;
            }

            string folder = path.Contains('/') ? path[..path.IndexOf('/')] : "";
            switch (folder)
            {
                case "skills":
                    foreach (SkillDto dto in Items<SkillDto>(yaml, path, text, d => d.Id, seenIn, errors))
                    {
                        skills.Add(new SkillDef(dto.Id!, dto.Tags ?? []));
                    }

                    break;
                case "encounters":
                    encounters.AddRange(Items<EncounterDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "objectives":
                    objectives.AddRange(Items<ObjectiveDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "crew":
                    crew.AddRange(Items<CrewDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "gear":
                    gear.AddRange(Items<GearDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "assignments":
                    assignments.AddRange(Items<AssignmentDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "tuning":
                    errors.Add(new ContentError(path, $"Unknown tuning file. The only tuning files are {TuningFile} and {CrewTuningFile}."));
                    break;
                default:
                    errors.Add(new ContentError(path, $"Unknown content folder '{folder}'."));
                    break;
            }
        }

        List<EncounterDef> encounterDefs = BuildEncounters(encounters, skills, errors);
        List<ObjectiveDef> objectiveDefs = BuildObjectives(objectives, encounterDefs, errors);
        (MissionTuning? tuning, BenchTuning? bench) = LoadTuning(yaml, files, errors);
        CrewTuning? crewTuning = LoadCrewTuning(yaml, files, errors);
        List<CrewDef> crewDefs = BuildCrew(crew, skills, crewTuning, errors);
        List<GearDef> gearDefs = BuildGear(gear, errors);
        List<AssignmentDef> assignmentDefs = BuildAssignments(assignments, objectives.Select(o => o.Dto).ToList(), errors);

        ContentPack? pack = errors.Count == 0
            ? new ContentPack(manifest!.Id!, manifest.Version, skills, encounterDefs, objectiveDefs, tuning!, bench!, crewDefs, gearDefs, assignmentDefs, crewTuning!)
            : null;
        return new ContentLoadResult(pack, errors);
    }

    public static ContentLoadResult LoadDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return new ContentLoadResult(null, [new ContentError(root, "Content folder not found.")]);
        }

        Dictionary<string, string> files = Directory
            .EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText);
        return Load(files);
    }

    // The valid, uniquely named items of one list file. Ids are unique across the whole pack.
    private static List<T> Items<T>(IDeserializer yaml, string path, string text, Func<T, string?> id, Dictionary<string, string> seenIn, List<ContentError> errors)
        where T : class
    {
        var items = new List<T>();
        foreach (T? dto in Parse<List<T?>>(yaml, path, text, errors) ?? [])
        {
            if (dto is null)
            {
                errors.Add(new ContentError(path, "A list item is blank."));
                continue;
            }

            if (!CheckId(path, id(dto), errors))
            {
                continue;
            }

            if (seenIn.TryGetValue(id(dto)!, out string? first))
            {
                errors.Add(new ContentError(path, $"Duplicate id '{id(dto)}', first defined in {first}."));
                continue;
            }

            seenIn[id(dto)!] = path;
            items.Add(dto);
        }

        return items;
    }

    private static T? Parse<T>(IDeserializer yaml, string file, string text, List<ContentError> errors)
        where T : class
    {
        try
        {
            T? value = yaml.Deserialize<T>(text);
            if (value is null)
            {
                errors.Add(new ContentError(file, "The file is empty."));
            }

            return value;
        }
        catch (YamlException e)
        {
            errors.Add(new ContentError(file, $"Line {e.Start.Line}: {e.InnerException?.Message ?? e.Message}"));
            return null;
        }
    }

    private static bool CheckId(string file, string? id, List<ContentError> errors)
    {
        if (id is not null && IdPattern().IsMatch(id))
        {
            return true;
        }

        errors.Add(new ContentError(file, $"Id '{id}' must be upper case letters, digits, and underscores, starting with a letter."));
        return false;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex IdPattern();

    private sealed class PackDto
    {
        public string? Id { get; set; }

        public int Version { get; set; }
    }

    private sealed class SkillDto
    {
        public string? Id { get; set; }

        public List<string>? Tags { get; set; }
    }
}

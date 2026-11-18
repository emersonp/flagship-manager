using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;
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
        var lines = new List<(string File, LineDto Dto)>();
        var symbols = new List<(string File, SymbolDto Dto)>();
        var pronouns = new List<(string File, PronounDto Dto)>();
        var storylets = new List<(string File, StoryletDto Dto)>();
        var vignettes = new List<(string File, StoryletDto Dto)>();
        var traits = new List<(string File, TraitDto Dto)>();
        var seenIn = new Dictionary<string, string>();
        // Symbols and pronoun sets are named in text, apart from the ids of everything else.
        var symbolsSeenIn = new Dictionary<string, string>();
        var pronounsSeenIn = new Dictionary<string, string>();
        // Traits are typed into text and requirements, and keep lower-case ids (conventions: the one exception).
        var traitsSeenIn = new Dictionary<string, string>();
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
                        CheckTags(dto.Tags, message => errors.Add(new ContentError(path, $"{dto.Id}: {message}")));
                        skills.Add(new SkillDef(dto.Id!, dto.Tags ?? [], CheckName(path, dto.Id!, dto.Name, errors)));
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
                case "lines":
                    lines.AddRange(Items<LineDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "symbols":
                    symbols.AddRange(Items<SymbolDto>(yaml, path, text, d => d.Id, symbolsSeenIn, errors, CheckLowerId).Select(d => (path, d)));
                    break;
                case "pronouns":
                    pronouns.AddRange(Items<PronounDto>(yaml, path, text, d => d.Id, pronounsSeenIn, errors, CheckLowerId).Select(d => (path, d)));
                    break;
                case "storylets":
                    storylets.AddRange(Items<StoryletDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "vignettes":
                    vignettes.AddRange(Items<StoryletDto>(yaml, path, text, d => d.Id, seenIn, errors).Select(d => (path, d)));
                    break;
                case "traits":
                    traits.AddRange(Items<TraitDto>(yaml, path, text, d => d.Id, traitsSeenIn, errors, CheckLowerId).Select(d => (path, d)));
                    break;
                case "tuning":
                    errors.Add(new ContentError(path, $"Unknown tuning file. The only tuning files are {TuningFile} and {CrewTuningFile}."));
                    break;
                default:
                    errors.Add(new ContentError(path, $"Unknown content folder '{folder}'."));
                    break;
            }
        }

        // Traits first: every use of a trait in an encounter, a storylet, or a vignette is checked against its definition.
        List<TraitDef> traitDefs = BuildTraits(traits, errors);
        var book = new TraitBook(traitDefs);
        List<GearDef> gearDefs = BuildGear(gear, errors);
        List<EncounterDef> encounterDefs = BuildEncounters(encounters, skills, gearDefs, book, errors);
        List<ObjectiveDef> objectiveDefs = BuildObjectives(objectives, encounterDefs, errors);
        (MissionTuning? tuning, BenchTuning? bench, CycleNumbers cycle) = LoadTuning(yaml, files, errors);
        CrewTuning? crewTuning = LoadCrewTuning(yaml, files, errors);
        List<PronounSet> pronounSets = BuildPronouns(pronouns, errors);
        List<CrewDef> crewDefs = BuildCrew(crew, skills, crewTuning, pronounSets, errors);
        List<AssignmentDef> assignmentDefs = BuildAssignments(assignments, objectives.Select(o => o.Dto).ToList(), errors);
        List<SymbolDef> symbolDefs = BuildSymbols(symbols, errors);
        // Storylets and vignettes are read in the expression language (1e part four), against the traits, skills, and history.
        var expressions = new ExpressionBook(traitDefs, skills.Select(s => s.Id), HistoryKinds.All, Math.Max(1, cycle.HistoryCycles));
        List<StoryletDef> storyletDefs = BuildStorylets(storylets, book, expressions, errors);
        List<VignetteDef> vignetteDefs = BuildVignettes(vignettes, book, expressions, errors);
        List<LineDef> lineDefs = BuildLines(lines, encounters, objectives, symbolDefs, skills, encounterDefs, objectiveDefs, storylets.Concat(vignettes).Select(s => s.Dto.Id!), errors);
        lineDefs.AddRange(BuildOwnedLines(encounters, objectives, symbolDefs, skills, errors));
        lineDefs.AddRange(BuildStoryletLines(storylets, symbolDefs, skills, errors));
        lineDefs.AddRange(BuildStoryletLines(vignettes, symbolDefs, skills, errors));
        Dictionary<string, IReadOnlyDictionary<string, string>> choiceLabels = BuildChoiceLabels(encounters, errors);

        ContentPack? pack = errors.Count == 0
            ? new ContentPack(manifest!.Id!, manifest.Version, skills, encounterDefs, objectiveDefs, tuning!, bench!, crewDefs, gearDefs, assignmentDefs, crewTuning!)
            {
                Lines = lineDefs,
                Symbols = symbolDefs,
                Pronouns = pronounSets,
                ChoiceLabels = choiceLabels,
                Traits = traitDefs,
                Storylets = storyletDefs,
                StoryletsPerCycle = cycle.StoryletsPerCycle,
                Vignettes = vignetteDefs,
                VignettesPerCycle = cycle.VignettesPerCycle,
                VignetteCooldown = cycle.VignetteCooldown,
                CaptainsTimePerCycle = cycle.CaptainsTime,
                HistoryCycles = cycle.HistoryCycles,
                CastTries = cycle.CastTries,
            }
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
    private static List<T> Items<T>(IDeserializer yaml, string path, string text, Func<T, string?> id, Dictionary<string, string> seenIn, List<ContentError> errors, Func<string, string?, List<ContentError>, bool>? checkId = null)
        where T : class
    {
        checkId ??= CheckId;
        var items = new List<T>();
        foreach (T? dto in Parse<List<T?>>(yaml, path, text, errors) ?? [])
        {
            if (dto is null)
            {
                errors.Add(new ContentError(path, "A list item is blank."));
                continue;
            }

            if (!checkId(path, id(dto), errors))
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

    // The trimmed display name, or null when absent or refused. An unusable name is a loader error naming the file and the id.
    private static string? CheckName(string file, string id, string? name, List<ContentError> errors)
    {
        if (name is null)
        {
            return null;
        }

        string trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 80 || trimmed.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) >= 0)
        {
            errors.Add(new ContentError(file, $"{id}: name must be 1 to 80 characters with no line break."));
            return null;
        }

        return trimmed;
    }

    private static bool CheckId(string file, string? id, List<ContentError> errors)
    {
        if (id is not null && Ids.IsItemId(id))
        {
            return true;
        }

        errors.Add(new ContentError(file, $"Id '{id}' must be {Said(Ids.ItemRule)}"));
        return false;
    }

    // A rule string read mid-sentence: "Lower case: ..." becomes "lower case: ...".
    // The key effects and requirements wrote before slice 1e, part three.
    private const string FlagIsNowTrait = "'flag' is now 'trait' (GDD section 15, item 40).";

    private static string Said(string rule)
    {
        return char.ToLowerInvariant(rule[0]) + rule[1..];
    }

    // Tags are matched exactly, so each follows the inner rule like every id typed into text.
    private static void CheckTags(IEnumerable<string>? tags, Action<string> error)
    {
        foreach (string tag in tags ?? [])
        {
            if (!Ids.IsInnerId(tag))
            {
                error($"Tag '{tag}' must be {Said(Ids.InnerRule)}");
            }
        }
    }

    private sealed class PackDto
    {
        public string? Id { get; set; }

        public int Version { get; set; }
    }

    private sealed class SkillDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string>? Tags { get; set; }
    }
}

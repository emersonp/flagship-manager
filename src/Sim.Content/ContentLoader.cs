using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sim.Content;

public static partial class ContentLoader
{
    private const string ManifestFile = "pack.yaml";
    private static readonly string[] KnownFolders = ["skills"];

    // Keys are paths relative to the pack root, with forward slashes.
    public static ContentLoadResult Load(IReadOnlyDictionary<string, string> files)
    {
        var errors = new List<ContentError>();
        IDeserializer yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

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
        var seenIn = new Dictionary<string, string>();
        foreach ((string path, string text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (path == ManifestFile)
            {
                continue;
            }

            string folder = path.Contains('/') ? path[..path.IndexOf('/')] : "";
            if (!KnownFolders.Contains(folder))
            {
                errors.Add(new ContentError(path, $"Unknown content folder '{folder}'."));
                continue;
            }

            foreach (SkillDto dto in Parse<List<SkillDto>>(yaml, path, text, errors) ?? [])
            {
                if (!CheckId(path, dto.Id, errors))
                {
                    continue;
                }

                if (seenIn.TryGetValue(dto.Id, out string? first))
                {
                    errors.Add(new ContentError(path, $"Duplicate id '{dto.Id}', first defined in {first}."));
                    continue;
                }

                seenIn[dto.Id] = path;
                skills.Add(new SkillDef(dto.Id, dto.Tags));
            }
        }

        ContentPack? pack = errors.Count == 0 ? new ContentPack(manifest!.Id!, manifest.Version, skills) : null;
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
        public string Id { get; set; } = "";

        public List<string> Tags { get; set; } = [];
    }
}

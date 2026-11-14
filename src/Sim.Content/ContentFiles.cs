using Sim.Core.Content;

namespace Sim.Content;

// The text of every content file, by path relative to the content root ("crew/starting.yaml"), with forward slashes.
public sealed record ContentFileSet(IReadOnlyDictionary<string, string> Files)
{
    // Reads every .yaml file under the root, keyed as ContentLoader.LoadDirectory keys them. A missing folder gives an empty set,
    // and Validate on an empty set gives the loader's own errors.
    public static ContentFileSet ReadDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return new ContentFileSet(new Dictionary<string, string>());
        }

        return new ContentFileSet(Directory
            .EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText));
    }

    public ContentLoadResult Validate() => ContentLoader.Load(Files);

    // Every item the files hold, leniently read: an item with no id is skipped and a file that does not parse lists nothing (Validate reports it).
    public IReadOnlyList<ContentItemRef> Index()
    {
        var items = new List<ContentItemRef>();
        foreach (ContentKind kind in Enum.GetValues<ContentKind>())
        {
            foreach (string path in ContentFolders.FilesOf(this, kind))
            {
                List<ContentReader.Entry> entries = ContentReader.ReadEntries(kind, Files[path]) ?? [];
                HashSet<int> lossy = ContentFaithfulness.Analyse(kind, path, Files[path])?.Select(l => l.Index).ToHashSet() ?? [];
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Model is { } item)
                    {
                        items.Add(new ContentItemRef(kind, item.Id, item.Name?.Trim(), path, lossy.Contains(i)));
                    }
                }
            }
        }

        return items;
    }
}

// Which folder holds which kind, as the loader decides it, and where a new item goes when its kind has no file.
internal static class ContentFolders
{
    public static string Folder(ContentKind kind) => kind switch
    {
        ContentKind.Skill => "skills",
        ContentKind.Gear => "gear",
        ContentKind.Crew => "crew",
        ContentKind.Assignment => "assignments",
        ContentKind.Objective => "objectives",
        ContentKind.Encounter => "encounters",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string ConventionalFile(ContentKind kind) => kind == ContentKind.Crew ? "crew/starting.yaml" : $"{Folder(kind)}/base.yaml";

    // Paths in loader order: the first is where a new item goes.
    public static IEnumerable<string> FilesOf(ContentFileSet files, ContentKind kind)
    {
        string prefix = Folder(kind) + "/";
        return files.Files.Keys.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal);
    }
}

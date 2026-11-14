using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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

    // Worked out once per set: a request reads the disk once and asks it many questions.
    public ContentLoadResult Validate() => ContentFileCache.Of(this).Validation;

    // Every item the files hold, leniently read: an item with no id is skipped and a file that does not parse lists nothing (Validate reports it).
    public IReadOnlyList<ContentItemRef> Index() => ContentFileCache.Of(this).Index;
}

// What a set's text reads as, parsed once per file and kept with the set. A set is never changed (an edit makes a new one), so what
// is kept here always matches its text. Edits that rewrite a file read it themselves, so they never share a list with a reader.
internal sealed class ContentFileCache
{
    private static readonly ConditionalWeakTable<ContentFileSet, ContentFileCache> Caches = new();

    private readonly ContentFileSet files;
    private readonly ConcurrentDictionary<string, Lazy<ContentReader.FileRead?>> reads = new();
    private readonly ConcurrentDictionary<string, Lazy<List<(int Index, ContentLoss Loss)>?>> analyses = new();
    private readonly ConcurrentDictionary<ContentKind, Lazy<Dictionary<string, IContentModel>>> byId = new();
    private readonly Lazy<ContentLoadResult> validation;
    private readonly Lazy<IReadOnlyList<ContentItemRef>> index;

    private ContentFileCache(ContentFileSet files)
    {
        this.files = files;
        validation = new Lazy<ContentLoadResult>(() => ContentLoader.Load(files.Files));
        index = new Lazy<IReadOnlyList<ContentItemRef>>(BuildIndex);
    }

    public static ContentFileCache Of(ContentFileSet files) => Caches.GetValue(files, f => new ContentFileCache(f));

    public ContentLoadResult Validation => validation.Value;

    public IReadOnlyList<ContentItemRef> Index => index.Value;

    public ContentReader.FileRead? Read(ContentKind kind, string path)
    {
        return reads.GetOrAdd(path, p => new Lazy<ContentReader.FileRead?>(() => ContentReader.ReadEntries(kind, files.Files[p]))).Value;
    }

    // The whole file's losses, as ContentFaithfulness.Analyse gives them with nothing excluded.
    public List<(int Index, ContentLoss Loss)>? Analysis(ContentKind kind, string path)
    {
        return analyses.GetOrAdd(path, p => new Lazy<List<(int Index, ContentLoss Loss)>?>(() => ContentFaithfulness.Analyse(kind, p, files.Files[p]))).Value;
    }

    // The first item with the id in loader order, as ContentEdits.Get has always found it.
    public IContentModel? Item(ContentKind kind, string id)
    {
        return byId.GetOrAdd(kind, k => new Lazy<Dictionary<string, IContentModel>>(() => BuildItems(k))).Value.GetValueOrDefault(id);
    }

    private Dictionary<string, IContentModel> BuildItems(ContentKind kind)
    {
        var items = new Dictionary<string, IContentModel>();
        foreach (string path in ContentFolders.FilesOf(files, kind))
        {
            foreach (ContentReader.Entry entry in Read(kind, path)?.Entries ?? [])
            {
                if (entry.Model is { } model)
                {
                    items.TryAdd(model.Id, model);
                }
            }
        }

        return items;
    }

    private IReadOnlyList<ContentItemRef> BuildIndex()
    {
        var items = new List<ContentItemRef>();
        foreach (ContentKind kind in Enum.GetValues<ContentKind>())
        {
            foreach (string path in ContentFolders.FilesOf(files, kind))
            {
                List<ContentReader.Entry> entries = Read(kind, path)?.Entries ?? [];
                HashSet<int> lossy = Analysis(kind, path)?.Select(l => l.Index).ToHashSet() ?? [];
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

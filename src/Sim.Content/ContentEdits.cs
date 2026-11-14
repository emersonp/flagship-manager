namespace Sim.Content;

// Changes to a file set, all in memory except Write. Only the file an edit touches gets new text.
public static class ContentEdits
{
    private static readonly Dictionary<ContentKind, Type> ModelTypes = new()
    {
        [ContentKind.Skill] = typeof(SkillModel),
        [ContentKind.Gear] = typeof(GearModel),
        [ContentKind.Crew] = typeof(CrewModel),
        [ContentKind.Assignment] = typeof(AssignmentModel),
        [ContentKind.Objective] = typeof(ObjectiveModel),
        [ContentKind.Encounter] = typeof(EncounterModel),
    };

    public static T? Get<T>(ContentFileSet files, ContentKind kind, string id)
        where T : class
    {
        foreach (string path in ContentFolders.FilesOf(files, kind))
        {
            IContentModel? item = ContentReader.ReadItems(kind, files.Files[path])?.FirstOrDefault(i => i.Id == id);
            if (item is not null)
            {
                return item as T;
            }
        }

        return null;
    }

    // Replaces the item with the model's id in the file that holds it, or adds it to the kind's first file that parses (the conventional file if the kind has none).
    public static ContentFileSet Upsert(ContentFileSet files, ContentKind kind, object model)
    {
        if (model is not IContentModel item || model.GetType() != ModelTypes[kind])
        {
            throw new ArgumentException($"A {kind} needs a {ModelTypes[kind].Name}.", nameof(model));
        }

        string? target = null;
        foreach (string path in ContentFolders.FilesOf(files, kind))
        {
            List<IContentModel>? items = ContentReader.ReadItems(kind, files.Files[path]);
            if (items is null)
            {
                continue;
            }

            int at = items.FindIndex(i => i.Id == item.Id);
            if (at >= 0)
            {
                items[at] = item;
                return Replace(files, path, Rewrite(files.Files[path], items));
            }

            target ??= path;
        }

        if (target is null)
        {
            target = ContentFolders.ConventionalFile(kind);
            if (files.Files.ContainsKey(target))
            {
                throw new InvalidOperationException($"{target} does not parse, so the new {kind} has nowhere to go.");
            }

            return Replace(files, target, Rewrite("", [item]));
        }

        List<IContentModel> held = ContentReader.ReadItems(kind, files.Files[target])!;
        held.Add(item);
        return Replace(files, target, Rewrite(files.Files[target], held));
    }

    // The set itself comes back when nothing holds the id.
    public static ContentFileSet Delete(ContentFileSet files, ContentKind kind, string id)
    {
        foreach (string path in ContentFolders.FilesOf(files, kind))
        {
            List<IContentModel>? items = ContentReader.ReadItems(kind, files.Files[path]);
            if (items is not null && items.RemoveAll(i => i.Id == id) > 0)
            {
                return Replace(files, path, Rewrite(files.Files[path], items));
            }
        }

        return files;
    }

    // For the manifest and the tuning files, which are edited as text.
    public static ContentFileSet ReplaceText(ContentFileSet files, string path, string text)
    {
        CheckPath(path);
        return Replace(files, path, text);
    }

    // Every list file rewritten in the canonical layout. A file that does not parse is left as it is.
    public static ContentFileSet Canonicalize(ContentFileSet files)
    {
        ContentFileSet result = files;
        foreach (ContentKind kind in Enum.GetValues<ContentKind>())
        {
            foreach (string path in ContentFolders.FilesOf(files, kind))
            {
                List<IContentModel>? items = ContentReader.ReadItems(kind, files.Files[path]);
                if (items is not null)
                {
                    result = Replace(result, path, Rewrite(files.Files[path], items));
                }
            }
        }

        return result;
    }

    // Writes only the files that differ from before, each through a temporary file and a rename; deletes nothing.
    public static void Write(string root, ContentFileSet before, ContentFileSet after)
    {
        foreach (string path in after.Files.Keys)
        {
            CheckPath(path);
        }

        foreach ((string path, string text) in after.Files)
        {
            if (before.Files.TryGetValue(path, out string? old) && old == text)
            {
                continue;
            }

            string full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string temp = full + ".tmp";
            try
            {
                File.WriteAllText(temp, text);
                File.Move(temp, full, overwrite: true);
            }
            catch
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }

                throw;
            }
        }
    }

    private static string Rewrite(string oldText, IReadOnlyList<IContentModel> items) => ContentWriter.Render(ContentWriter.LeadingComment(oldText), items);

    private static ContentFileSet Replace(ContentFileSet files, string path, string text)
    {
        var copy = new Dictionary<string, string>(files.Files) { [path] = text };
        return new ContentFileSet(copy);
    }

    // A path stays inside the root: relative, forward slashes, no "..", and a .yaml file.
    private static void CheckPath(string path)
    {
        bool bad = path.Length == 0
            || Path.IsPathRooted(path)
            || path.StartsWith('/')
            || path.Contains('\\')
            || !path.EndsWith(".yaml", StringComparison.Ordinal)
            || path.Split('/').Any(part => part is "" or "." or "..");
        if (bad)
        {
            throw new ArgumentException($"'{path}' is not a relative .yaml path inside the content folder.", nameof(path));
        }
    }
}

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
        [ContentKind.Line] = typeof(LineModel),
        [ContentKind.Symbol] = typeof(SymbolModel),
        [ContentKind.Pronoun] = typeof(PronounModel),
        [ContentKind.Storylet] = typeof(StoryletModel),
        [ContentKind.Vignette] = typeof(VignetteModel),
        [ContentKind.Trait] = typeof(TraitModel),
        [ContentKind.TestShip] = typeof(TestShipModel),
    };

    // Read from the set's own parse of each file, so asking for every item of a kind parses each file once.
    public static T? Get<T>(ContentFileSet files, ContentKind kind, string id)
        where T : class
    {
        return ContentFileCache.Of(files).Item(kind, id) as T;
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
                Guard(kind, path, files.Files[path], item.Id, firstOnly: true);
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
                throw new InvalidOperationException($"{target} cannot be read, so nothing can be added to it.");
            }

            return Replace(files, target, Rewrite("", [item]));
        }

        Guard(kind, target, files.Files[target], null, firstOnly: false);
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
                Guard(kind, path, files.Files[path], id, firstOnly: false);
                return Replace(files, path, Rewrite(files.Files[path], items));
            }
        }

        return files;
    }

    // Deleting a skill also takes its key from every starting crew member who has it: the one edit that changes more than one file.
    // Each member is replaced in turn, so each rewrite is checked for losses like any other. The set itself comes back when nothing holds the skill.
    public static ContentFileSet DeleteSkill(ContentFileSet files, string id)
    {
        if (Get<SkillModel>(files, ContentKind.Skill, id) is null)
        {
            return files;
        }

        ContentFileSet result = files;
        foreach (string path in ContentFolders.FilesOf(files, ContentKind.Crew))
        {
            foreach (CrewModel crew in ContentReader.ReadItems(ContentKind.Crew, files.Files[path])?.OfType<CrewModel>().Where(c => c.Skills.ContainsKey(id)) ?? [])
            {
                CrewModel current = Get<CrewModel>(result, ContentKind.Crew, crew.Id)!;
                result = Upsert(result, ContentKind.Crew, current with { Skills = current.Skills.Where(s => s.Key != id).ToDictionary(s => s.Key, s => s.Value) });
            }
        }

        return Delete(result, ContentKind.Skill, id);
    }

    // For the manifest and the tuning files, which are edited as text.
    public static ContentFileSet ReplaceText(ContentFileSet files, string path, string text)
    {
        CheckPath(path);
        return Replace(files, path, text);
    }

    // What a rewrite of this file would lose, item by item; empty when the file is faithful or is not a list file. A file-level problem
    // (a top-level map, a second document) appears only here, with a null Item, and not as ContentItemRef.Lossy, so a caller must check both.
    public static IReadOnlyList<ContentLoss> Losses(ContentFileSet files, string path)
    {
        ContentKind? kind = Enum.GetValues<ContentKind>().Cast<ContentKind?>().FirstOrDefault(k => path.StartsWith(ContentFolders.Folder(k!.Value) + "/", StringComparison.Ordinal));
        if (kind is null || !files.Files.TryGetValue(path, out string? text))
        {
            return [];
        }

        List<(int Index, ContentLoss Loss)>? found = ContentFileCache.Of(files).Analysis(kind.Value, path);
        return found is null ? [new ContentLoss(path, null, "does not parse as a list of items.")] : found.Select(l => l.Loss).ToList();
    }

    // Refuses a rewrite that would change anything but layout in any item other than the one being replaced or deleted.
    private static void Guard(ContentKind kind, string path, string text, string? excludeId, bool firstOnly)
    {
        List<ContentLoss>? losses = ContentFaithfulness.Analyse(kind, path, text, excludeId, firstOnly)?.Select(l => l.Loss).ToList();
        if (losses is { Count: > 0 })
        {
            throw new ContentLossException(losses);
        }
    }

    // Every list file rewritten in the canonical layout. A file that does not parse is left as it is.
    public static ContentFileSet Canonicalize(ContentFileSet files)
    {
        var losses = new List<ContentLoss>();
        foreach (ContentKind kind in Enum.GetValues<ContentKind>())
        {
            foreach (string path in ContentFolders.FilesOf(files, kind))
            {
                losses.AddRange(ContentFaithfulness.Analyse(kind, path, files.Files[path])?.Select(l => l.Loss) ?? []);
            }
        }

        if (losses.Count > 0)
        {
            throw new ContentLossException(losses);
        }

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
    // A file that starts with a byte-order mark keeps it: reading the text drops the mark, so it is put back here.
    public static void Write(string root, ContentFileSet before, ContentFileSet after)
    {
        foreach (string path in after.Files.Keys)
        {
            CheckPath(path);
        }

        List<KeyValuePair<string, string>> changed = after.Files.Where(f => !(before.Files.TryGetValue(f.Key, out string? old) && old == f.Value)).ToList();
        foreach (KeyValuePair<string, string> file in changed)
        {
            CheckNoLinks(root, file.Key);
        }

        foreach ((string path, string text) in changed)
        {
            string full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string temp = full + ".tmp";
            bool marked = HasByteOrderMark(full);
            try
            {
                File.WriteAllText(temp, text, new System.Text.UTF8Encoding(marked));
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

    private static bool HasByteOrderMark(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using FileStream stream = File.OpenRead(path);
        Span<byte> head = stackalloc byte[3];
        return stream.ReadAtLeast(head, 3, throwOnEndOfStream: false) == 3 && head.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    // A file that used CRLF keeps it, so a save is not a whole-file diff.
    private static string Rewrite(string oldText, IReadOnlyList<IContentModel> items)
    {
        string text = ContentWriter.Render(ContentWriter.LeadingComment(oldText), items);
        return oldText.Contains("\r\n", StringComparison.Ordinal) ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
    }

    private static ContentFileSet Replace(ContentFileSet files, string path, string text)
    {
        var copy = new Dictionary<string, string>(files.Files) { [path] = text };
        return new ContentFileSet(copy);
    }

    // Nothing is written through a link: no folder on the way below the root, and not the file itself.
    private static void CheckNoLinks(string root, string path)
    {
        string current = root;
        foreach (string part in path.Split('/'))
        {
            current = Path.Combine(current, part);
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
            {
                throw new ArgumentException($"'{path}' goes through a link at '{part}'.", nameof(path));
            }
        }
    }

    // A path stays inside the root: relative, forward slashes, no "..", and a .yaml file.
    private static void CheckPath(string path)
    {
        bool bad = path.Length == 0
            || Path.IsPathRooted(path)
            || path.StartsWith('/')
            || path.Contains('\\')
            || !path.EndsWith(".yaml", StringComparison.Ordinal)
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0
            || path.Split('/').Any(part => part is "" or "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0);
        if (bad)
        {
            throw new ArgumentException($"'{path}' is not a relative .yaml path inside the content folder.", nameof(path));
        }
    }
}

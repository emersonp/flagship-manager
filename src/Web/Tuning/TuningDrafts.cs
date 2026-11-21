using System.Security.Cryptography;
using System.Text;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;

namespace Web.Tuning;

// A draft of one tuning file (1f part one, Task 7; design 4.2): the file's text and hash as the draft began, and the changed values by
// the loader's key path (a dice or range field's two values together, as TuningFields names them).
public sealed record TuningDraft(string File, string BaseText, string BaseHash, DateTimeOffset Began, IReadOnlyDictionary<string, IReadOnlyList<string>> Values);

// The draft as the loader reads it: the tuning when it loads, and otherwise each error beside the field it names, the rest apart.
public sealed record DraftLoad(MissionTuning? Tuning, BenchTuning? Bench, IReadOnlyDictionary<string, string> FieldErrors, IReadOnlyList<string> OtherErrors);

// The drafts of the tuning files, held in the server's memory (design 4.2 and 4.3): one per file, shared by every tab, cleared by Save or
// Discard, and lost on restart. The YAML in content/ is the only stored tuning: a save puts the draft's values into the file on disk in
// place (TuningFields.Replace) and goes through ContentEditor.Save, so the whole pack is loaded with it before the file is written.
public sealed class TuningDrafts(ContentEditor editor, GameServices services)
{
    public const string Missions = "tuning/missions.yaml";

    // The files a draft can be held for: the mission tuning alone in part one (part two adds the rest).
    public static readonly IReadOnlyList<string> Files = [Missions];

    private readonly object gate = new();
    private readonly Dictionary<string, TuningDraft> drafts = [];

    // Raised with the file whenever its draft changes, is discarded, or is saved: a run on the draft cancels itself (Task 10).
    public event Action<string>? Changed;

    public TuningDraft? Current(string file)
    {
        Check(file);
        lock (gate)
        {
            return drafts.GetValueOrDefault(file);
        }
    }

    // Sets one field's values, beginning a draft from the file on disk if there is none. The saved value takes the path out of the draft,
    // and a draft with no values is gone. Throws for a path that is not a field of the file, or the wrong count of values for it; the values
    // themselves are judged by Load, as the loader judges them.
    public TuningDraft? Set(string file, string path, IReadOnlyList<string> values)
    {
        Check(file);
        bool changed;
        TuningDraft? result;
        lock (gate)
        {
            TuningDraft draft = drafts.GetValueOrDefault(file) ?? Begin(file, DiskText(file) ?? throw new InvalidOperationException($"The pack has no {file}."));
            TuningField field = TuningFields.Read(draft.BaseText).SelectMany(s => s.Fields).FirstOrDefault(f => f.Path == path)
                ?? throw new ArgumentException($"{path} is not a field of {file}.", nameof(path));
            if (values.Count != field.Values.Count)
            {
                throw new ArgumentException($"{path} takes {field.Values.Count} value(s), not {values.Count}.", nameof(values));
            }

            var next = new Dictionary<string, IReadOnlyList<string>>(draft.Values);
            if (values.SequenceEqual(field.Values))
            {
                next.Remove(path);
            }
            else
            {
                next[path] = values.ToArray();
            }

            bool had = drafts.TryGetValue(file, out TuningDraft? before);
            changed = !had || !SameValues(before!.Values, next);
            if (next.Count == 0)
            {
                drafts.Remove(file);
                result = null;
                changed = had;
            }
            else
            {
                result = draft with { Values = next };
                drafts[file] = result;
            }
        }

        if (changed)
        {
            Changed?.Invoke(file);
        }

        return result;
    }

    public void Discard(string file)
    {
        Check(file);
        bool had;
        lock (gate)
        {
            had = drafts.Remove(file);
        }

        if (had)
        {
            Changed?.Invoke(file);
        }
    }

    // True when the file on disk is no longer the text the draft began from (an edit in the text view, by hand, or another save). The
    // changed keys are the fields whose value on disk differs from the draft's base text, in the base's order; an edit to a comment alone
    // makes a draft stale with none. A file that no longer reads as fields has every field of the base changed.
    public bool Stale(string file, out IReadOnlyList<string> changedKeys)
    {
        changedKeys = [];
        if (Current(file) is not { } draft)
        {
            return false;
        }

        string? disk = DiskText(file);
        if (disk is not null && Hash(disk) == draft.BaseHash)
        {
            return false;
        }

        changedKeys = ChangedKeys(draft.BaseText, disk);
        return true;
    }

    // The base text with the draft's values put in, through the loader (LoadTuningText). With no draft, the file on disk as it is.
    public DraftLoad Load(string file)
    {
        Check(file);
        TuningDraft? draft = Current(file);
        string text = draft?.BaseText ?? DiskText(file) ?? "";
        IReadOnlyDictionary<string, IReadOnlyList<string>> values = draft?.Values ?? new Dictionary<string, IReadOnlyList<string>>();
        string replaced;
        try
        {
            replaced = TuningFields.Replace(text, values);
        }
        catch (ArgumentException)
        {
            // A value that cannot be put in as a plain scalar (a '#', a line break): find each one and put its refusal beside it.
            var refused = new Dictionary<string, string>();
            foreach ((string path, IReadOnlyList<string> value) in values)
            {
                try
                {
                    TuningFields.Replace(text, new Dictionary<string, IReadOnlyList<string>> { [path] = value });
                }
                catch (ArgumentException e)
                {
                    refused[path] = Message(e);
                }
            }

            return new DraftLoad(null, null, refused, refused.Count == 0 ? ["The values do not read back as plain scalars."] : []);
        }

        (MissionTuning? tuning, BenchTuning? bench, IReadOnlyList<ContentError> errors) = ContentLoader.LoadTuningText(replaced);
        (IReadOnlyDictionary<string, string> fieldErrors, IReadOnlyList<string> otherErrors) = Place(file, replaced, errors);
        return new DraftLoad(tuning, bench, fieldErrors, otherErrors);
    }

    // Puts the draft's values into the file on disk and saves it through the editor, so the whole pack is loaded with it and the live
    // pack is swapped as for any edit (a mission in flight keeps its tuning, GDD 4.7, because its state carries it). Refused when the
    // draft is stale, with an error for each changed key (placed beside it by Place); a saved draft is cleared.
    public async Task<SaveResult> Save(string file, CancellationToken cancellation)
    {
        Check(file);
        if (Current(file) is not { } draft)
        {
            return new SaveResult(false, [new ContentError(file, "There is no draft to save.")]);
        }

        if (Stale(file, out IReadOnlyList<string> changedKeys))
        {
            return new SaveResult(false, StaleErrors(file, changedKeys));
        }

        string? written = null;
        SaveResult result = await editor.Save(files =>
        {
            // Read again under the editor's lock: a write that got in since the check above refuses the save too.
            string disk = files.Files.GetValueOrDefault(file) ?? throw new ChangeRefusedException($"The pack has no {file}.");
            if (Hash(disk) != draft.BaseHash)
            {
                throw new ChangeRefusedException(StaleMessage(file));
            }

            try
            {
                written = TuningFields.Replace(disk, draft.Values);
            }
            catch (ArgumentException e)
            {
                throw new ChangeRefusedException(Message(e));
            }

            return ContentEdits.ReplaceText(files, file, written);
        }, cancellation);
        if (!result.Saved)
        {
            return result;
        }

        lock (gate)
        {
            // A value set while the save ran stays in the draft, now on the text just written.
            if (drafts.GetValueOrDefault(file) is { } now && !ReferenceEquals(now, draft))
            {
                TuningDraft rebased = Rebase(now, written!);
                if (rebased.Values.Count > 0)
                {
                    drafts[file] = rebased;
                }
                else
                {
                    drafts.Remove(file);
                }
            }
            else
            {
                drafts.Remove(file);
            }
        }

        Changed?.Invoke(file);
        return result;
    }

    // Each error beside the field it names (design 4.2): a message that begins with a field's path (the TuningReader's and CheckRanges's
    // do, as "check.assistDivisor must be ...", or "bench.teams.rookie.skill.0 must not be above ...", whose field is the range) goes to
    // that field. One whose path names a group of fields rather than a field ("check.tiers must fall from critical to failure.") goes to
    // the first field in it. An error with no path is one of the rest, and one in another file is named by its file. Two errors on one
    // field are joined.
    public static (IReadOnlyDictionary<string, string> FieldErrors, IReadOnlyList<string> OtherErrors) Place(string file, string text, IReadOnlyList<ContentError> errors)
    {
        IReadOnlyList<TuningField> fields;
        try
        {
            fields = TuningFields.Read(text).SelectMany(s => s.Fields).ToList();
        }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or FormatException)
        {
            fields = [];
        }

        var placed = new Dictionary<string, string>();
        var other = new List<string>();
        foreach (ContentError error in errors)
        {
            if ((error.File == file || error.File == "") && PathOf(fields, error.Message) is { } path)
            {
                placed[path] = placed.TryGetValue(path, out string? before) ? $"{before} {error.Message}" : error.Message;
            }
            else
            {
                other.Add(error.File == file || error.File == "" ? error.Message : $"{error.File}: {error.Message}");
            }
        }

        return (placed, other);
    }

    private static string? PathOf(IReadOnlyList<TuningField> fields, string message)
    {
        // The longest field path the message begins with, followed by a space or by a value's index ('.0', '.count').
        string? field = fields.Select(f => f.Path)
            .Where(p => message.StartsWith(p, StringComparison.Ordinal) && message.Length > p.Length && message[p.Length] is ' ' or '.')
            .MaxBy(p => p.Length);
        if (field is not null)
        {
            return field;
        }

        // A group's path ("check.tiers"): the first field under it.
        int space = message.IndexOf(' ', StringComparison.Ordinal);
        string group = space > 0 ? message[..space] : message;
        return fields.FirstOrDefault(f => f.Path.StartsWith(group + ".", StringComparison.Ordinal))?.Path;
    }

    private static IReadOnlyList<ContentError> StaleErrors(string file, IReadOnlyList<string> changedKeys)
    {
        return [new ContentError(file, StaleMessage(file)), .. changedKeys.Select(k => new ContentError(file, $"{k} has changed in the file since this draft began."))];
    }

    private static string StaleMessage(string file)
    {
        return $"{file} has changed since this draft began, so it is not saved. Discard the draft, or copy its values over by hand.";
    }

    private static IReadOnlyList<string> ChangedKeys(string baseText, string? disk)
    {
        IReadOnlyList<TuningField> was = TuningFields.Read(baseText).SelectMany(s => s.Fields).ToList();
        IReadOnlyList<TuningField> now;
        try
        {
            now = disk is null ? [] : TuningFields.Read(disk).SelectMany(s => s.Fields).ToList();
        }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or FormatException)
        {
            return was.Select(f => f.Path).ToList();
        }

        Dictionary<string, TuningField> byPath = now.ToDictionary(f => f.Path);
        var changed = was.Where(f => !byPath.TryGetValue(f.Path, out TuningField? n) || !n.Values.SequenceEqual(f.Values)).Select(f => f.Path).ToList();
        HashSet<string> known = was.Select(f => f.Path).ToHashSet();
        changed.AddRange(now.Where(f => !known.Contains(f.Path)).Select(f => f.Path));
        return changed;
    }

    // The draft begun again on the text just saved, keeping each value that still differs from it.
    private TuningDraft Rebase(TuningDraft draft, string text)
    {
        Dictionary<string, TuningField> fields = TuningFields.Read(text).SelectMany(s => s.Fields).ToDictionary(f => f.Path);
        Dictionary<string, IReadOnlyList<string>> values = draft.Values
            .Where(v => !fields.TryGetValue(v.Key, out TuningField? f) || !f.Values.SequenceEqual(v.Value))
            .ToDictionary(v => v.Key, v => v.Value);
        return Begin(draft.File, text) with { Values = values };
    }

    private TuningDraft Begin(string file, string text)
    {
        return new TuningDraft(file, text, Hash(text), services.Clock.GetUtcNow(), new Dictionary<string, IReadOnlyList<string>>());
    }

    private string? DiskText(string file)
    {
        return editor.Read().Files.GetValueOrDefault(file);
    }

    private static bool SameValues(IReadOnlyDictionary<string, IReadOnlyList<string>> a, IReadOnlyDictionary<string, IReadOnlyList<string>> b)
    {
        return a.Count == b.Count && a.All(v => b.TryGetValue(v.Key, out IReadOnlyList<string>? other) && other.SequenceEqual(v.Value));
    }

    private static string Message(ArgumentException e)
    {
        // An ArgumentException's message carries " (Parameter 'values')" after the words.
        string suffix = $" (Parameter '{e.ParamName}')";
        return e.ParamName is not null && e.Message.EndsWith(suffix, StringComparison.Ordinal) ? e.Message[..^suffix.Length] : e.Message;
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static void Check(string file)
    {
        if (!Files.Contains(file))
        {
            throw new ArgumentException($"{file} is not a tuning file a draft can be held for.", nameof(file));
        }
    }
}

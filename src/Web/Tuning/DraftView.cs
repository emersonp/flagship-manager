using System.Globalization;
using Sim.Content;
using Sim.Core.Content;

namespace Web.Tuning;

// One field of the draft form: its values in the draft (or the file's, unchanged), the saved values when the draft changed them, and the
// loader's error beside it. HintAtHead: the field's hint is the comment of the group it opens (Task 3's "a nested key's comment is its
// first field's hint"), so the form shows it at the group's head rather than under the field.
public sealed record DraftFieldView(TuningField Field, IReadOnlyList<string> Values, IReadOnlyList<string>? Saved, string? Error, bool HintAtHead)
{
    // The element that holds the row, patched on its own when the field changes.
    public string RowId => Web.Forms.FormValues.Slug(Field.Path) + "-row";

    // The name each value is posted under: the path for a number, and the loader's own names for a dice field's and a range's two.
    public IReadOnlyList<string> Names => DraftView.Names(Field);
}

public abstract record DraftItem;

public sealed record DraftFieldItem(DraftFieldView Field) : DraftItem;

// A nested map: its key, the comment at its head, and what is under it in file order.
public sealed record DraftGroup(string Path, string Key, string? Hint, IReadOnlyList<DraftItem> Items) : DraftItem;

// The draft's line: how many fields it changes, and when it began in the server's local time ("14:32", "today").
public sealed record DraftLineView(int Count, string Time, string Day);

public sealed record DraftSectionView(string Name, string? Note, bool ReadOnly, IReadOnlyList<DraftItem> Items, int Count);

// What the draft form is drawn from (1f part one, Task 9; design 4.2): the file's fields in file order, read from the draft's base text or,
// with no draft, the file on disk; the draft's line, the stale keys, and the result of the last Save.
public sealed record DraftView(
    string File,
    string? Unreadable,
    IReadOnlyList<DraftSectionView> Sections,
    TuningDraft? Draft,
    DraftLineView? DraftLine,
    IReadOnlyList<string>? StaleKeys,
    IReadOnlyList<KeyValuePair<string, string>>? Saved,
    bool Refused,
    IReadOnlyList<string> Problems)
{
    // The sections the mission bench cannot see (design 4.2), read-only until Legacy mode measures them (part two removes this).
    public static readonly IReadOnlyList<string> ReadOnlySections = ["storylets", "salience", "vignettes", "captainsTime", "history"];

    public const string ReadOnlyLabel = "Measured in Legacy mode (part two); edit as text for now.";

    // The text view of the file.
    public static string TextHref(string file) => file == TuningDrafts.Missions ? "/dev/content/files/missions" : "/dev/content";

    public static IReadOnlyList<string> Names(TuningField field)
    {
        return field.Kind switch
        {
            TuningFieldKind.Dice => [field.Path + ".count", field.Path + ".sides"],
            TuningFieldKind.Range => [field.Path + ".0", field.Path + ".1"],
            _ => [field.Path],
        };
    }

    // A field's values in words: "2", "2 dice of 6 sides", "1 to 3".
    public static string Shown(TuningFieldKind kind, IReadOnlyList<string> values)
    {
        return kind switch
        {
            TuningFieldKind.Dice => $"{values[0]} dice of {values[1]} sides",
            TuningFieldKind.Range => $"{values[0]} to {values[1]}",
            _ => values[0],
        };
    }

    // Saved: the values a save just wrote, for the notice. Refusal: a save the editor refused, its errors placed beside their fields.
    public static DraftView Build(TuningDrafts drafts, ContentEditor editor, TimeProvider clock, string file, IReadOnlyList<KeyValuePair<string, string>>? saved = null, SaveResult? refusal = null)
    {
        TuningDraft? draft = drafts.Current(file);
        string text = draft?.BaseText ?? editor.Read().Files.GetValueOrDefault(file) ?? "";
        IReadOnlyList<TuningSection> sections;
        try
        {
            sections = TuningFields.Read(text);
        }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or FormatException)
        {
            return new DraftView(file, $"{file} does not read as a tuning file ({e.Message}), so it has no form. Fix it in the text view.", [], draft, Line(draft, clock), null, saved, refusal is not null, refusal?.Errors.Select(e => e.Message).ToList() ?? []);
        }

        DraftLoad load = drafts.Load(file);
        var errors = new Dictionary<string, string>(load.FieldErrors);
        var problems = new List<string>(load.OtherErrors);
        if (refusal is not null)
        {
            (IReadOnlyDictionary<string, string> placed, IReadOnlyList<string> other) = TuningDrafts.Place(file, text, refusal.Errors);
            foreach ((string path, string message) in placed)
            {
                errors[path] = message;
            }

            problems = other.Concat(problems.Where(p => !other.Contains(p))).ToList();
        }

        IReadOnlyList<string>? stale = drafts.Stale(file, out IReadOnlyList<string> keys) ? keys : null;
        string[] lines = text.Split('\n');
        var views = new List<DraftSectionView>();
        foreach (TuningSection section in sections)
        {
            var fields = new List<(DraftFieldView View, string[] Segments)>();
            string[]? previous = null;
            foreach (TuningField field in section.Fields)
            {
                string[] segments = field.Path == section.Name ? [field.Key] : field.Key.Split('.');
                IReadOnlyList<string> values = draft?.Values.GetValueOrDefault(field.Path) ?? field.Values;
                bool changed = !values.SequenceEqual(field.Values);

                // The first field of a group, whose own line carries no comment: its hint is the group's key comment.
                bool opens = segments.Length > 1 && (previous is null || !previous.AsSpan(0, previous.Length - 1).SequenceEqual(segments.AsSpan(0, segments.Length - 1)));
                bool ownComment = field.Line - 1 < lines.Length && lines[field.Line - 1].Contains('#', StringComparison.Ordinal);
                fields.Add((new DraftFieldView(field, values, changed ? field.Values : null, errors.GetValueOrDefault(field.Path), opens && !ownComment && field.Hint is not null), segments));
                previous = segments;
            }

            views.Add(new DraftSectionView(section.Name, section.Note, ReadOnlySections.Contains(section.Name), Items(fields, 0, section.Name), section.Fields.Count));
        }

        return new DraftView(file, null, views, draft, Line(draft, clock), stale, saved, refusal is not null, problems);
    }

    private static List<DraftItem> Items(IReadOnlyList<(DraftFieldView View, string[] Segments)> fields, int depth, string prefix)
    {
        var items = new List<DraftItem>();
        int i = 0;
        while (i < fields.Count)
        {
            string[] segments = fields[i].Segments;
            if (segments.Length == depth + 1)
            {
                items.Add(new DraftFieldItem(fields[i].View));
                i++;
                continue;
            }

            string key = segments[depth];
            int end = i;
            while (end < fields.Count && fields[end].Segments.Length > depth + 1 && fields[end].Segments[depth] == key)
            {
                end++;
            }

            List<(DraftFieldView View, string[] Segments)> under = fields.Skip(i).Take(end - i).ToList();
            DraftFieldView first = under[0].View;
            string? hint = first.HintAtHead && under[0].Segments.Length == depth + 2 ? first.Field.Hint : null;
            items.Add(new DraftGroup($"{prefix}.{key}", key, hint, Items(under, depth + 1, $"{prefix}.{key}")));
            i = end;
        }

        return items;
    }

    // "1 change. Begun at 14:32 today, and held in the server's memory: a restart loses it."
    private static DraftLineView? Line(TuningDraft? draft, TimeProvider clock)
    {
        if (draft is null)
        {
            return null;
        }

        DateTimeOffset began = TimeZoneInfo.ConvertTime(draft.Began, clock.LocalTimeZone);
        DateTimeOffset now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone);
        string day = began.Date == now.Date ? "today" : "on " + began.ToString("d MMMM", CultureInfo.InvariantCulture);
        return new DraftLineView(draft.Values.Count, began.ToString("HH:mm", CultureInfo.InvariantCulture), day);
    }
}

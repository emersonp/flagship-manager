using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Sim.Content;

// One thing the editor could not keep in a file. Item is the id, or "entry N" when the entry has none; null for the file as a whole.
public sealed record ContentLoss(string File, string? Item, string Problem)
{
    public override string ToString() => $"{File}: {(Item is null ? "" : Item + " ")}{Problem}";
}

// Thrown instead of rewriting a file the editor cannot carry faithfully; nothing has been changed.
public sealed class ContentLossException(IReadOnlyList<ContentLoss> losses) : Exception(string.Join("\n", losses.Select(l => l.ToString())))
{
    public IReadOnlyList<ContentLoss> Losses { get; } = losses;
}

// Decides whether a list file can be rewritten without changing anything but layout. The file's entries are compared, as plain
// trees, with the text the writer would produce from the models read from them. The comparison is blind to key order and to the
// equivalences listed under Normalize; anything else that differs is a loss.
internal static class ContentFaithfulness
{
    private abstract record Tree;

    private sealed record Scalar(string? Value) : Tree;

    private sealed record Seq(IReadOnlyList<Tree> Items) : Tree;

    private sealed record Map(IReadOnlyDictionary<string, Tree> Entries) : Tree;

    // Optional keys that read the same absent, null, or empty.
    // Storylets, vignettes, and traits (1e part six) add notes, cast, cooldown, effects, default, check, success, failure, and a trait's
    // shown, on, levels, max, briefing, launch and its lists, mission, every, expires, and onTargetDied.
    private static readonly HashSet<string> NullKeys = ["name", "chain", "gearTag", "ifUnattended", "decision", "tags", "bold", "safe", "stages", "objectives", "skills", "offer", "draw", "pronouns", "text", "lines", "labels", "approaches", "requires", "outcomes", "chronicle",
        "notes", "cast", "cooldown", "global", "effects", "default", "check", "success", "failure", "toward", "on", "shown", "levels", "max", "briefing", "launch", "sent", "notSent", "withTarget", "mission", "every", "expires", "onTargetDied",
        "traits", "history", "storylets", "subjects", "date", "cycle", "daysAgo", "cyclesAgo", "value", "fit", "aboard", "alive"];
    private static readonly HashSet<string> EmptyListKeys = ["tags", "bold", "safe", "stages", "lines", "approaches", "requires", "effects", "sent", "notSent", "withTarget", "onTargetDied", "traits", "storylets", "subjects"];
    private static readonly HashSet<string> EmptyMapKeys = ["skills", "offer", "objectives", "draw", "text", "labels", "outcomes", "cast", "history"];

    // Properties the loader deserialises as bool: any case of true and false.
    private static readonly HashSet<string> FlagKeys = ["skippable", "commsBlocked", "plural", "withTarget"];

    // Read as a bool like FlagKeys, but true when absent, so false is kept (a trait's shown).
    private static readonly HashSet<string> TrueFlagKeys = ["shown", "fit", "aboard", "alive"];

    // Properties the loader deserialises as int (YAML integers: 0x10, 1_0, 0010 as octal).
    private static readonly HashSet<string> YamlIntKeys = ["seniority", "caution", "discipline", "loadoutLimit", "need", "stakes", "difficulty", "level", "weight", "cast", "global", "weeks", "checkBonus", "date", "cycle", "daysAgo", "cyclesAgo"];

    // Values the loader reads from the raw text with int.TryParse (effects, teamSize, a stage's count).
    private static readonly HashSet<string> RawIntKeys = ["progress", "momentum", "strain", "add", "set", "teamSize", "count", "min", "max", "loyalty", "morale"];

    // Numbers that read as 0 when absent.
    private static readonly HashSet<string> ZeroKeys = ["seniority", "caution", "discipline", "loadoutLimit", "need", "stakes", "difficulty", "teamSize", "count", "checkBonus"];

    // Maps whose keys the loader reads as YAML integers: an assignment's offer and a trait's levels.
    private static readonly HashSet<string> IntKeyedMaps = ["offer", "levels"];

    private static readonly HashSet<string> CountKeys = ["teamSize", "count"];

    // Each loss as (entry index, loss). Null when the text does not parse as a list. A file-level problem has index -1.
    public static List<(int Index, ContentLoss Loss)>? Analyse(ContentKind kind, string path, string text, string? excludeId = null, bool excludeFirstOnly = false)
    {
        ContentReader.FileRead? read = ContentReader.ReadEntries(kind, text);
        if (read is null)
        {
            return null;
        }

        List<ContentReader.Entry> entries = read.Entries;
        var losses = new List<(int, ContentLoss)>();
        if (read.ExtraDocuments)
        {
            losses.Add((-1, new ContentLoss(path, null, "has a second document, which the editor cannot keep.")));
        }

        if (InnerComment(kind, text) is { } comment)
        {
            losses.Add((-1, new ContentLoss(path, null, comment)));
        }

        bool excluded = false;
        for (int i = 0; i < entries.Count; i++)
        {
            ContentReader.Entry entry = entries[i];
            if (excludeId is not null && entry.RawId == excludeId && !(excludeFirstOnly && excluded))
            {
                excluded = true;
                continue;
            }

            string label = entry.Model?.Id ?? (entry.RawId is { Length: > 0 } ? entry.RawId : $"entry {i + 1}");
            if (entry.Model is null)
            {
                string problem = entry.Node is YamlScalarNode { Value: null or "" or "~" or "null" } ? "is a blank entry." : entry.Node is not YamlMappingNode ? "is not a mapping, so the editor cannot keep it." : "has no id, so the editor cannot keep it.";
                losses.Add((i, new ContentLoss(path, label, problem)));
                continue;
            }

            var problems = new List<string>();
            Tree original = Convert(entry.Node, null, null);
            Tree written = Convert(Rendered(entry.Model), null, null);
            Diff(original, written, "", problems);
            problems.AddRange(FormLosses(entry.Model));
            losses.AddRange(problems.Select(p => (i, new ContentLoss(path, label, p))));
        }

        return losses;
    }

    // Kinds with a notes key, where an item's comment belongs (1e part six).
    private static readonly HashSet<ContentKind> NotesKinds = [ContentKind.Storylet, ContentKind.Vignette, ContentKind.Trait, ContentKind.Encounter];

    // The writer keeps the leading comment (ContentWriter.LeadingComment) and drops every other, so a comment after it is a loss. The
    // comments come from the YAML scanner, so a # inside a quoted or block scalar ("#subject#") is not one. Null when there is none.
    private static string? InnerComment(ContentKind kind, string text)
    {
        int leading = LeadingLines(text);
        var lines = new List<int>();
        try
        {
            var scanner = new Scanner(new StringReader(text), skipComments: false);
            while (scanner.MoveNext())
            {
                if (scanner.Current is YamlDotNet.Core.Tokens.Comment comment && comment.Start.Line > leading && !lines.Contains((int)comment.Start.Line))
                {
                    lines.Add((int)comment.Start.Line);
                }
            }
        }
        catch (YamlException)
        {
            // The reader has already parsed it; a scanner that disagrees reports what it found before.
        }

        if (lines.Count == 0)
        {
            return null;
        }

        string more = lines.Count == 1 ? "" : $" (and {lines.Count - 1} more)";
        string where = NotesKinds.Contains(kind) ? "move it into notes" : "move it into the comment at the top of the file";
        return $"has a comment on line {lines[0]}{more} that saving from a form would drop; {where}, or edit the file as text.";
    }

    // How many lines ContentWriter.LeadingComment reads as the leading comment, counting the blank lines before it.
    private static int LeadingLines(string text)
    {
        string[] lines = text.TrimStart('﻿').Split('\n');
        int last = 0;
        bool started = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.StartsWith('#'))
            {
                started = true;
                last = i + 1;
            }
            else if (line.Length > 0 || started)
            {
                break;
            }
        }

        return last;
    }

    // What the writer keeps but the encounter form cannot: it holds one trait change a tier, so a save from it would drop the rest.
    private static IEnumerable<string> FormLosses(IContentModel model)
    {
        if (model is not EncounterModel encounter)
        {
            yield break;
        }

        for (int i = 0; i < encounter.Approaches.Count; i++)
        {
            foreach ((string tier, IReadOnlyList<EffectModel> effects) in encounter.Approaches[i].Outcomes)
            {
                if (effects.Count(e => e.Trait is not null) > 1)
                {
                    yield return $"has an outcome with more than one trait change, which the form cannot keep: approaches[{i}].outcomes.{tier}.";
                }
            }
        }
    }

    private static YamlNode Rendered(IContentModel model)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(ContentWriter.Render(null, [model])));
        return ((YamlSequenceNode)stream.Documents[0].RootNode).Children[0];
    }

    // key is the mapping key the node sits under; parent is the key of the collection it is an element or value of.
    private static Tree Convert(YamlNode node, string? key, string? parent)
    {
        if (!node.Tag.IsEmpty)
        {
            return new Scalar($"{node.Tag} (an explicit tag)");
        }

        switch (node)
        {
            case YamlScalarNode scalar:
                return ScalarOf(scalar, key, parent);
            case YamlSequenceNode sequence:
                List<Tree> items = sequence.Children.Select(c => Convert(c, null, key)).ToList();
                if (key is not null && CountKeys.Contains(key) && items.Count == 2 && items[0] == items[1])
                {
                    return items[0];
                }

                return new Seq(items);
            case YamlMappingNode mapping:
                var entries = new Dictionary<string, Tree>();
                foreach ((YamlNode k, YamlNode v) in mapping.Children)
                {
                    string name = k is YamlScalarNode { Value: { } text } ? text : "<not a plain key>";
                    if (key is not null && IntKeyedMaps.Contains(key) && ContentReader.YamlInt(name) is { } number)
                    {
                        name = number.ToString(CultureInfo.InvariantCulture);
                    }

                    Tree value = Convert(v, name, key);
                    if (!Droppable(name, value))
                    {
                        entries[name] = value;
                    }
                }

                return new Map(entries);
            default:
                return new Scalar(node.ToString());
        }
    }

    // Normalize: a plain null, ~, or empty value is null. Beyond that each value is read as the loader reads that key: a bool property
    // ignores case; an int property is a YAML integer; effect values, teamSize, and count are int.TryParse of the raw text; the rest are strings.
    private static Scalar ScalarOf(YamlScalarNode node, string? key, string? parent)
    {
        string? value = node.Value;
        if (node.Style == ScalarStyle.Plain && value is null or "" or "~" or "null" or "Null" or "NULL")
        {
            return new Scalar(null);
        }

        if (key is not null && (FlagKeys.Contains(key) || TrueFlagKeys.Contains(key)) && ContentReader.IsBool(value) is { } flag)
        {
            return new Scalar(flag ? "true" : "false");
        }

        bool rawInt = (key is not null && RawIntKeys.Contains(key)) || (parent is not null && CountKeys.Contains(parent));
        bool yamlInt = (key is not null && YamlIntKeys.Contains(key)) || parent is "skills" or "offer";
        if (rawInt && ContentReader.RawInt(value) is { } raw)
        {
            return new Scalar(raw.ToString(CultureInfo.InvariantCulture));
        }

        if (yamlInt && ContentReader.YamlInt(value) is { } n)
        {
            return new Scalar(n.ToString(CultureInfo.InvariantCulture));
        }

        return new Scalar(value);
    }

    // Normalize: for these known optional keys, an absent value, a null, a false flag, a zero, or an empty list or map read the same.
    private static bool Droppable(string key, Tree value)
    {
        return (NullKeys.Contains(key) && value is Scalar { Value: null })
            || (FlagKeys.Contains(key) && value is Scalar { Value: "false" })
            || (ZeroKeys.Contains(key) && value is Scalar { Value: "0" })
            || (EmptyListKeys.Contains(key) && value is Seq { Items.Count: 0 })
            || (EmptyMapKeys.Contains(key) && value is Map { Entries.Count: 0 });
    }

    private static void Diff(Tree a, Tree b, string path, List<string> problems)
    {
        string at = path.Length == 0 ? "the entry" : path;
        switch (a, b)
        {
            case (Map ma, Map mb):
                foreach ((string key, Tree value) in ma.Entries)
                {
                    string sub = path.Length == 0 ? key : $"{path}.{key}";
                    if (mb.Entries.TryGetValue(key, out Tree? other))
                    {
                        Diff(value, other, sub, problems);
                    }
                    else
                    {
                        problems.Add($"has a key the editor cannot keep: {sub}.");
                    }
                }

                foreach (string key in mb.Entries.Keys.Where(k => !ma.Entries.ContainsKey(k)))
                {
                    problems.Add($"would gain a key the file does not have: {(path.Length == 0 ? key : $"{path}.{key}")}.");
                }

                break;
            case (Seq sa, Seq sb):
                for (int i = 0; i < Math.Min(sa.Items.Count, sb.Items.Count); i++)
                {
                    Diff(sa.Items[i], sb.Items[i], $"{path}[{i}]", problems);
                }

                for (int i = sb.Items.Count; i < sa.Items.Count; i++)
                {
                    problems.Add($"has a list entry the editor cannot keep: {path}[{i}].");
                }

                for (int i = sa.Items.Count; i < sb.Items.Count; i++)
                {
                    problems.Add($"would gain a list entry the file does not have: {path}[{i}].");
                }

                break;
            case (Scalar sa, Scalar sb):
                if (sa.Value != sb.Value)
                {
                    problems.Add($"has a value the editor cannot keep: {at} is '{sa.Value}'.");
                }

                break;
            default:
                problems.Add($"has a value the editor cannot keep: {at}.");
                break;
        }
    }
}

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
    private static readonly HashSet<string> NullKeys = ["name", "chain", "gearTag", "ifUnattended", "decision", "tags", "bold", "safe", "stages", "objectives", "skills", "offer", "draw", "pronouns", "use"];
    private static readonly HashSet<string> EmptyListKeys = ["tags", "bold", "safe", "stages"];
    private static readonly HashSet<string> EmptyMapKeys = ["skills", "offer", "objectives", "draw"];

    // Properties the loader deserialises as bool: any case of true and false.
    private static readonly HashSet<string> FlagKeys = ["skippable", "commsBlocked", "plural"];

    // Properties the loader deserialises as int (YAML integers: 0x10, 1_0, 0010 as octal).
    private static readonly HashSet<string> YamlIntKeys = ["seniority", "caution", "discipline", "loadoutLimit", "need", "stakes", "difficulty"];

    // Values the loader reads from the raw text with int.TryParse (effects, teamSize, a stage's count).
    private static readonly HashSet<string> RawIntKeys = ["progress", "momentum", "strain", "teamSize", "count", "min", "max"];

    // Numbers that read as 0 when absent.
    private static readonly HashSet<string> ZeroKeys = ["seniority", "caution", "discipline", "loadoutLimit", "need", "stakes", "difficulty", "teamSize", "count"];

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
            losses.AddRange(problems.Select(p => (i, new ContentLoss(path, label, p))));
        }

        return losses;
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
                    if (key == "offer" && ContentReader.YamlInt(name) is { } number)
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

        if (key is not null && FlagKeys.Contains(key) && ContentReader.IsBool(value) is { } flag)
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

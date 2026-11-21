using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using CommentToken = YamlDotNet.Core.Tokens.Comment;

namespace Sim.Content;

// Dice: a { count, sides } map. Range: a two-number sequence, as bench skill: [1, 3].
public enum TuningFieldKind { Number, Dice, Range }

// One scalar leaf of a tuning file (a dice map or a range is one field of two values). Path is the loader's dotted key path; Key is the
// path inside its section; Hint is the comment trailing its line; Line is the 1-based line of its key.
public sealed record TuningField(string Path, string Section, string Key, TuningFieldKind Kind, IReadOnlyList<string> Values, string? Hint, int Line);

// A top-level key of a tuning file, its note (the comment on its line or the comment lines just above it), and its fields in file order.
public sealed record TuningSection(string Name, string? Note, IReadOnlyList<TuningField> Fields);

// A tuning file's fields read from its own text, and their values replaced in place (slice 1f, design 4.2 and 4.3), so a save from the
// form keeps every comment, space, key order, and flow map it did not change. The loader judges the values; nothing here does.
public static class TuningFields
{
    // Throws YamlException when the text does not parse, and FormatException when it is not a map of maps and scalars.
    public static IReadOnlyList<TuningSection> Read(string text)
    {
        return Parse(text).Sections;
    }

    // Replaces each path's scalar(s) in place; throws for a path that is not a field of the text, or a value that would not read back as written.
    public static string Replace(string text, IReadOnlyDictionary<string, IReadOnlyList<string>> values)
    {
        Parsed parsed = Parse(text);
        var splices = new List<(int Start, int End, string Value)>();
        foreach ((string path, IReadOnlyList<string> newValues) in values)
        {
            if (!parsed.Spans.TryGetValue(path, out IReadOnlyList<(int Start, int End)>? spans))
            {
                throw new ArgumentException($"{path} is not a field of this tuning file.", nameof(values));
            }

            if (newValues.Count != spans.Count)
            {
                throw new ArgumentException($"{path} takes {spans.Count} value(s), not {newValues.Count}.", nameof(values));
            }

            for (int i = 0; i < spans.Count; i++)
            {
                if (newValues[i].Length == 0 || newValues[i].Any(char.IsControl))
                {
                    throw new ArgumentException($"{path} cannot be '{newValues[i]}'.", nameof(values));
                }

                splices.Add((spans[i].Start, spans[i].End, newValues[i]));
            }
        }

        // From the last in the file to the first, so each earlier span is still where it was read.
        string result = text;
        foreach ((int start, int end, string value) in splices.OrderByDescending(s => s.Start))
        {
            result = string.Concat(result.AsSpan(0, start), value, result.AsSpan(end));
        }

        // A value that is not a plain scalar (a '#', a ': ', a bracket) would change the file's shape: read it back to be sure it did not.
        Dictionary<string, TuningField> after;
        try
        {
            after = Parse(result).Sections.SelectMany(s => s.Fields).ToDictionary(f => f.Path);
        }
        catch (Exception e) when (e is YamlException or FormatException)
        {
            throw new ArgumentException($"The values do not read back as plain scalars: {e.Message}", nameof(values), e);
        }

        foreach ((string path, IReadOnlyList<string> newValues) in values)
        {
            if (!after.TryGetValue(path, out TuningField? field) || !field.Values.SequenceEqual(newValues))
            {
                throw new ArgumentException($"{path} does not read back as '{string.Join(", ", newValues)}'.", nameof(values));
            }
        }

        return result;
    }

    private sealed record Parsed(IReadOnlyList<TuningSection> Sections, IReadOnlyDictionary<string, IReadOnlyList<(int Start, int End)>> Spans);

    // The file as a small tree, with each scalar's span in the text.
    private abstract record Node;

    private sealed record ScalarNode(string Value, int Start, int End, int Line) : Node;

    private sealed record MapNode(List<(ScalarNode Key, Node Value)> Entries) : Node;

    private sealed record SequenceNode(List<Node> Items) : Node;

    // A comment as written: its line, whether code comes before it on that line, its text after '#', and whether it continues the comment
    // above (two or more spaces after the '#', as on boldPriorityWeight's line).
    private sealed record CommentLine(int Line, bool Inline, string Text, bool Continues);

    private static Parsed Parse(string text)
    {
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        int LineOf(long index)
        {
            int found = lineStarts.BinarySearch((int)index);
            return (found >= 0 ? found : ~found - 1) + 1;
        }

        // Comments, from the scanner (so a '#' inside a quoted scalar is not one), by line.
        var comments = new Dictionary<int, CommentLine>();
        var scanner = new Scanner(new StringReader(text), skipComments: false);
        while (scanner.MoveNext())
        {
            if (scanner.Current is CommentToken comment)
            {
                int at = (int)comment.Start.Index;
                int line = LineOf(at);
                int lineEnd = text.IndexOf('\n', at) is var n and >= 0 ? n : text.Length;
                string body = text[(at + 1)..lineEnd].TrimEnd('\r', ' ', '\t');
                bool inline = text.AsSpan(lineStarts[line - 1], at - lineStarts[line - 1]).Trim().Length > 0;
                comments[line] = new CommentLine(line, inline, body.Trim(), body.StartsWith("  ", StringComparison.Ordinal));
            }
        }

        var parser = new Parser(new StringReader(text));
        parser.Consume<StreamStart>();
        if (!parser.TryConsume<DocumentStart>(out _))
        {
            return new Parsed([], new Dictionary<string, IReadOnlyList<(int, int)>>());
        }

        Node root = ReadNode(parser, LineOf);
        if (root is not MapNode map)
        {
            throw new FormatException("A tuning file is a map of sections.");
        }

        // Who each comment belongs to: a field (its hint) or a section (its note). A continuation joins whatever the comment above it joined.
        int leadingEnd = 0;
        while (comments.TryGetValue(leadingEnd + 1, out CommentLine? c) && !c.Inline)
        {
            leadingEnd++;
        }

        var hints = new Dictionary<string, List<string>>();
        var owners = new Dictionary<int, string>();
        var fields = new List<(TuningField Field, IReadOnlyList<(int, int)> Spans)>();
        var sectionFields = new List<(string Name, List<string> Paths)>();

        void Own(string owner, int line)
        {
            // The comment lines just above a key, then the comment on its line.
            int first = line;
            while (first - 1 > leadingEnd && comments.TryGetValue(first - 1, out CommentLine? above) && !above.Inline && !owners.ContainsKey(first - 1))
            {
                first--;
            }

            for (int l = first; l <= line; l++)
            {
                if (comments.TryGetValue(l, out CommentLine? c) && !owners.ContainsKey(l))
                {
                    owners[l] = owner;
                }
            }
        }

        void Leaf(string section, string key, ScalarNode keyNode, TuningFieldKind kind, IReadOnlyList<ScalarNode> scalars, string? heldOwner)
        {
            // A section that is itself a leaf ("notable: 3") is one field named by the section.
            string path = key == "" ? section : $"{section}.{key}";
            fields.Add((new TuningField(path, section, key == "" ? section : key, kind, scalars.Select(s => s.Value).ToList(), null, keyNode.Line), scalars.Select(s => (s.Start, s.End)).ToList()));
            sectionFields[^1].Paths.Add(path);
            Own(path, keyNode.Line);
            if (heldOwner is not null)
            {
                // A nested map's key comment ("tiers:   # the lowest margin ...") is the hint of the first field under it (my call).
                foreach (int l in owners.Where(o => o.Value == heldOwner).Select(o => o.Key).ToList())
                {
                    owners[l] = path;
                }
            }
        }

        void Walk(string section, string prefix, ScalarNode keyNode, Node value, string? heldOwner)
        {
            switch (value)
            {
                case ScalarNode scalar:
                    Leaf(section, prefix, keyNode, TuningFieldKind.Number, [scalar], heldOwner);
                    break;
                case MapNode { Entries: [({ Value: "count" }, ScalarNode count), ({ Value: "sides" }, ScalarNode sides)] }:
                    Leaf(section, prefix, keyNode, TuningFieldKind.Dice, [count, sides], heldOwner);
                    break;
                case SequenceNode { Items: [ScalarNode low, ScalarNode high] }:
                    Leaf(section, prefix, keyNode, TuningFieldKind.Range, [low, high], heldOwner);
                    break;
                case MapNode inner:
                    string? held = heldOwner ?? $"\0{section}.{prefix}";
                    Own(held, keyNode.Line);
                    foreach ((ScalarNode key, Node child) in inner.Entries)
                    {
                        Walk(section, $"{prefix}.{key.Value}", key, child, held);
                        held = null;
                    }

                    break;
                case SequenceNode list:
                    throw new FormatException($"{section}.{prefix} on line {keyNode.Line} is a list of {list.Items.Count}; a tuning list is two numbers.");
            }
        }

        foreach ((ScalarNode key, Node value) in map.Entries)
        {
            sectionFields.Add((key.Value, []));
            if (value is MapNode { Entries: not [({ Value: "count" }, _), ({ Value: "sides" }, _)] } sectionMap)
            {
                Own($"\0{key.Value}", key.Line);
                foreach ((ScalarNode childKey, Node child) in sectionMap.Entries)
                {
                    Walk(key.Value, childKey.Value, childKey, child, null);
                }
            }
            else
            {
                Walk(key.Value, "", key, value, null);
            }
        }

        // Continuations join the owner of the comment above them; whatever is left unowned (a comment on its own between fields) is nobody's.
        foreach (CommentLine c in comments.Values.OrderBy(c => c.Line))
        {
            if (c.Continues && comments.ContainsKey(c.Line - 1) && owners.TryGetValue(c.Line - 1, out string? above))
            {
                owners[c.Line] = above;
            }
        }

        foreach ((int line, string owner) in owners.OrderBy(o => o.Key))
        {
            if (comments[line].Text.Length > 0)
            {
                (hints.TryGetValue(owner, out List<string>? list) ? list : hints[owner] = []).Add(comments[line].Text);
            }
        }

        string? Hint(string owner) => hints.TryGetValue(owner, out List<string>? list) ? string.Join(" ", list) : null;

        Dictionary<string, TuningField> byPath = fields.ToDictionary(f => f.Field.Path, f => f.Field with { Hint = Hint(f.Field.Path) });
        var sections = sectionFields
            .Select(s => new TuningSection(s.Name, Hint($"\0{s.Name}"), s.Paths.Select(p => byPath[p]).ToList()))
            .ToList();
        return new Parsed(sections, fields.ToDictionary(f => f.Field.Path, f => f.Spans));
    }

    private static Node ReadNode(IParser parser, Func<long, int> lineOf)
    {
        if (parser.TryConsume<YamlDotNet.Core.Events.Scalar>(out var scalar))
        {
            return new ScalarNode(scalar.Value, (int)scalar.Start.Index, (int)scalar.End.Index, lineOf(scalar.Start.Index));
        }

        if (parser.TryConsume<MappingStart>(out _))
        {
            var entries = new List<(ScalarNode, Node)>();
            while (!parser.TryConsume<MappingEnd>(out _))
            {
                if (ReadNode(parser, lineOf) is not ScalarNode key)
                {
                    throw new FormatException("A tuning file's keys are plain words.");
                }

                entries.Add((key, ReadNode(parser, lineOf)));
            }

            return new MapNode(entries);
        }

        if (parser.TryConsume<SequenceStart>(out _))
        {
            var items = new List<Node>();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                items.Add(ReadNode(parser, lineOf));
            }

            return new SequenceNode(items);
        }

        throw new FormatException($"Line {lineOf(parser.Current?.Start.Index ?? 0)} holds an anchor or alias, which a tuning file does not use.");
    }
}

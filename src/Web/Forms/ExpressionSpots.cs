using System.Globalization;
using System.Text.RegularExpressions;
using Sim.Core.Content;
using YamlDotNet.RepresentationModel;

namespace Web.Forms;

// Where an expression error is in a storylets or vignettes file as typed (1e part four, design 4.12): its line and column, counted
// from 1, and that line's text, to show it marked. Text has no line break.
public sealed record TextSpot(int Line, int Column, string Text);

// The loader says where an error is inside its expression ("STORYLET_02: cast.subject, at character 34: No trait named nosuch",
// ContentLoader.Storylets); the expression's own place in the file is found here, from the YAML as typed, which the loader read
// before it refused the expression. The character is exact when the expression's text stands as it is in the file (plain, or
// quoted with no escapes, on one line), as every expression in the packs does; otherwise the spot is where the value starts.
internal static partial class ExpressionSpots
{
    public static IReadOnlyDictionary<ContentError, TextSpot> Find(TextFile file, string typed, IReadOnlyList<ContentError> errors)
    {
        var spots = new Dictionary<ContentError, TextSpot>();
        if (file.Name is not ("storylets" or "vignettes"))
        {
            return spots;
        }

        string text = typed.Replace("\r\n", "\n", StringComparison.Ordinal);
        YamlSequenceNode? items = null;
        foreach (ContentError error in errors.Where(e => e.File == file.Path))
        {
            if (Error().Match(error.Message) is not { Success: true } match)
            {
                continue;
            }

            items ??= Items(text);
            if (items is null)
            {
                break;
            }

            if (Value(items, match) is { } value && Spot(text, value, int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture)) is { } spot)
            {
                spots.TryAdd(error, spot);
            }
        }

        return spots;
    }

    // Where a 0-based position is within one box's text (the expression widget's, 1e part six): its line and column, counted from 1,
    // and that line. Past the end is the end.
    public static TextSpot InBox(string text, int position)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        int offset = Math.Clamp(position, 0, text.Length);
        int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        int lineEnd = text.IndexOf('\n', offset);
        int line = text.AsSpan(0, lineStart).Count('\n') + 1;
        return new TextSpot(line, offset - lineStart + 1, text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)]);
    }

    // The file's items, or null when it does not read as a list.
    private static YamlSequenceNode? Items(string text)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlSequenceNode : null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    // The expression the key names, on the item with that id: cast.{slot}, requires, or options.{option}.requires.
    private static YamlScalarNode? Value(YamlSequenceNode items, Match match)
    {
        if (WithId(items, match.Groups["id"].Value) is not { } item)
        {
            return null;
        }

        if (match.Groups["slot"].Success)
        {
            return Child(item, "cast") is YamlMappingNode cast ? Child(cast, match.Groups["slot"].Value) as YamlScalarNode : null;
        }

        if (match.Groups["option"].Success)
        {
            return Child(item, "options") is YamlSequenceNode options && WithId(options, match.Groups["option"].Value) is { } option
                ? Child(option, "requires") as YamlScalarNode
                : null;
        }

        return Child(item, "requires") as YamlScalarNode;
    }

    private static YamlMappingNode? WithId(YamlSequenceNode items, string id)
    {
        return items.Children.OfType<YamlMappingNode>().FirstOrDefault(m => Child(m, "id") is YamlScalarNode { Value: { } value } && value == id);
    }

    private static YamlNode? Child(YamlMappingNode map, string key)
    {
        return map.Children.FirstOrDefault(c => c.Key is YamlScalarNode { Value: { } k } && k == key).Value;
    }

    // Character n of the value, counted from 1; past its end is its end.
    private static TextSpot? Spot(string text, YamlScalarNode value, int n)
    {
        int start = (int)value.Start.Index;
        int end = Math.Min((int)value.End.Index, text.Length);
        if (start < 0 || start > end)
        {
            return null;
        }

        string written = value.Value ?? "";
        int at = text.IndexOf(written, start, end - start, StringComparison.Ordinal);
        int offset = written.Length > 0 && at >= 0 ? at + Math.Clamp(n - 1, 0, written.Length) : start;
        int lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        int lineEnd = text.IndexOf('\n', offset);
        int line = text.AsSpan(0, lineStart).Count('\n') + 1;
        return new TextSpot(line, offset - lineStart + 1, text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)]);
    }

    [GeneratedRegex(@"^(?<id>[^:\s]+): (?:cast\.(?<slot>[a-z][a-z0-9_]*)|requires|options\.(?<option>[a-z][a-z0-9_]*)\.requires), at character (?<n>\d+): ")]
    private static partial Regex Error();
}

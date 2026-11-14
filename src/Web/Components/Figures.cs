using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Web.Bridge;

namespace Web.Components;

// Presentation helpers: the text stays as the read model wrote it; only figures and a few words gain a span.
public static partial class Figures
{
    // A standalone figure, never the digits inside an id such as CREW_01: a signed number, a score plus roll ("9 + 8"),
    // or a parenthesised margin. The sign may be the true minus.
    [GeneratedRegex(@"(?<![\w.])(\([+\u2212\-]?\d+\)|[+\u2212\-]?\d+(?:\.\d+)?(?: \+ \d+)?)(?![\w])")]
    private static partial Regex Number();

    [GeneratedRegex(@"\b(critical|success|partial|failure|disaster)\b")]
    private static partial Regex Tier();

    // Encodes the text and sets each standalone figure in the mono face.
    public static MarkupString Mono(string text)
    {
        return new MarkupString(MonoHtml(text));
    }

    // A line cut into parts: each name is encoded plain text, never scanned for figures; the text between is set as Mono does.
    public static MarkupString Mono(IReadOnlyList<LinePart> parts)
    {
        return new MarkupString(Join(parts, MonoHtml));
    }

    public static MarkupString Check(IReadOnlyList<LinePart> parts)
    {
        return new MarkupString(Join(parts, CheckHtml));
    }

    // The outcome is the last clause of the last part of text, so a name containing a comma cannot move it.
    public static MarkupString Injury(IReadOnlyList<LinePart> parts)
    {
        int last = parts.Count - 1;
        return new MarkupString(Join(parts, MonoHtml, last >= 0 && !parts[last].IsName ? last : -1, InjuryHtml));
    }

    // A check line: figures in mono, the result word in ink, or harm for a failure.
    public static MarkupString Check(string text)
    {
        return new MarkupString(CheckHtml(text));
    }

    private static string CheckHtml(string text)
    {
        var html = new StringBuilder();
        int at = 0;
        foreach (Match match in Tier().Matches(text))
        {
            html.Append(MonoHtml(text[at..match.Index]));
            string css = match.Value is "failure" or "disaster" ? "res res--fail" : "res";
            html.Append($"<span class=\"{css}\">{HtmlEncoder.Default.Encode(match.Value)}</span>");
            at = match.Index + match.Length;
        }

        html.Append(MonoHtml(text[at..]));
        return html.ToString();
    }

    // An injury line: the outcome, its last clause, is harm unless the member was unharmed.
    public static MarkupString Injury(string text)
    {
        return new MarkupString(InjuryHtml(text));
    }

    private static string InjuryHtml(string text)
    {
        int comma = text.LastIndexOf(", ", StringComparison.Ordinal);
        if (comma < 0 || !text.EndsWith('.'))
        {
            return MonoHtml(text);
        }

        string outcome = text[(comma + 2)..^1];
        string css = outcome == "unharmed" ? "res" : "res res--fail";
        return $"{MonoHtml(text[..(comma + 2)])}<span class=\"{css}\">{HtmlEncoder.Default.Encode(outcome)}</span>.";
    }

    private static string Join(IReadOnlyList<LinePart> parts, Func<string, string> text, int special = -1, Func<string, string>? specialText = null)
    {
        var html = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].IsName)
            {
                html.Append(HtmlEncoder.Default.Encode(parts[i].Text));
            }
            else
            {
                html.Append(i == special ? specialText!(parts[i].Text) : text(parts[i].Text));
            }
        }

        return html.ToString();
    }

    // "A", "A and B", "A, B, and C".
    public static string List(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
        };
    }

    // A list whose items never break across lines, so a long list wraps only between items.
    public static MarkupString Items(IReadOnlyList<string> items)
    {
        string[] spans = items.Select(i => $"<span class=\"nw\">{HtmlEncoder.Default.Encode(i)}</span>").ToArray();
        return new MarkupString(List(spans));
    }

    // "FullSuccess" as "Full success".
    public static string Words(string pascal)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]))
            {
                builder.Append(' ');
                builder.Append(char.ToLowerInvariant(pascal[i]));
            }
            else
            {
                builder.Append(pascal[i]);
            }
        }

        return builder.ToString();
    }

    private static string MonoHtml(string text)
    {
        var html = new StringBuilder();
        int at = 0;
        foreach (Match match in Number().Matches(text))
        {
            html.Append(HtmlEncoder.Default.Encode(text[at..match.Index]));
            html.Append($"<span class=\"m\">{HtmlEncoder.Default.Encode(match.Value)}</span>");
            at = match.Index + match.Length;
        }

        html.Append(HtmlEncoder.Default.Encode(text[at..]));
        return html.ToString();
    }
}

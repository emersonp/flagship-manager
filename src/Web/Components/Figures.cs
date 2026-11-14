using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

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

    // A check line: figures in mono, the result word in ink, or harm for a failure.
    public static MarkupString Check(string text)
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
        return new MarkupString(html.ToString());
    }

    // An injury line: the outcome, its last clause, is harm unless the member was unharmed.
    public static MarkupString Injury(string text)
    {
        int comma = text.LastIndexOf(", ", StringComparison.Ordinal);
        if (comma < 0 || !text.EndsWith('.'))
        {
            return Mono(text);
        }

        string outcome = text[(comma + 2)..^1];
        string css = outcome == "unharmed" ? "res" : "res res--fail";
        return new MarkupString($"{MonoHtml(text[..(comma + 2)])}<span class=\"{css}\">{HtmlEncoder.Default.Encode(outcome)}</span>.");
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

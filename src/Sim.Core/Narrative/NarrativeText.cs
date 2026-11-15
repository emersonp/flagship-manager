using System.Text.RegularExpressions;

namespace Sim.Core.Narrative;

// Text that breaks the token grammar; the message says how to write it.
public sealed class NarrativeFormatException(string message) : Exception(message);

// One #...# token. Head is a symbol, a person, or a thing. Form is a pronoun form; Singular and Plural are a verb choice.
public sealed record Token(string Raw, string Head, string? Form, string? Singular, string? Plural, bool Capitalize);

// A run of a line's text: literal text, or a token.
public sealed record TextPiece(string? Literal, Token? Token);

// The token grammar of line and symbol text, shared by the loader (which checks it) and the engine (which expands it).
// #head#, #head.form#, #head:singular|plural#, any of them ending .capitalize, and ## for a literal #.
public static partial class NarrativeText
{
    public static IReadOnlyList<TextPiece> Parse(string text)
    {
        var pieces = new List<TextPiece>();
        var literal = new System.Text.StringBuilder();
        int at = 0;
        while (at < text.Length)
        {
            char c = text[at];
            if (c != '#')
            {
                literal.Append(c);
                at++;
                continue;
            }

            if (at + 1 < text.Length && text[at + 1] == '#')
            {
                literal.Append('#');
                at += 2;
                continue;
            }

            int end = text.IndexOf('#', at + 1);
            if (end < 0)
            {
                throw new NarrativeFormatException($"A # at character {at + 1} is never closed. Write ## for a literal #.");
            }

            if (literal.Length > 0)
            {
                pieces.Add(new TextPiece(literal.ToString(), null));
                literal.Clear();
            }

            pieces.Add(new TextPiece(null, ParseToken(text[(at + 1)..end])));
            at = end + 1;
        }

        if (literal.Length > 0)
        {
            pieces.Add(new TextPiece(literal.ToString(), null));
        }

        return pieces;
    }

    private static Token ParseToken(string raw)
    {
        string body = raw;
        bool capitalize = false;
        if (body.EndsWith(".capitalize", StringComparison.Ordinal))
        {
            capitalize = true;
            body = body[..^".capitalize".Length];
        }

        int colon = body.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            string head = body[..colon];
            string[] forms = body[(colon + 1)..].Split('|');
            if (forms.Length != 2 || forms.Any(f => f.Length == 0 || f.Contains('#', StringComparison.Ordinal)))
            {
                throw new NarrativeFormatException($"#{raw}# needs two verb forms, singular then plural: #{head}:checks|check#.");
            }

            return new Token(raw, Head(head, raw), null, forms[0], forms[1], capitalize);
        }

        int dot = body.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
        {
            string form = body[(dot + 1)..];
            if (!NarrativeFacts.PronounForms.Contains(form))
            {
                throw new NarrativeFormatException($"#{raw}# asks for '{form}', which is not one of {string.Join(", ", NarrativeFacts.PronounForms)}, or capitalize.");
            }

            return new Token(raw, Head(body[..dot], raw), form, null, null, capitalize);
        }

        return new Token(raw, Head(body, raw), null, null, null, capitalize);
    }

    private static string Head(string head, string raw)
    {
        if (!HeadPattern().IsMatch(head))
        {
            throw new NarrativeFormatException($"#{raw}# must start with a name of letters, digits, and underscores.");
        }

        return head;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex HeadPattern();
}

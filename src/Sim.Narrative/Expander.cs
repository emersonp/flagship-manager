using System.Globalization;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Narrative;

// One run of expanded text: a name (plain text that is never scanned for figures) or text.
public sealed record TextPart(bool IsName, string Text);

// Thrown when text cannot be expanded against a situation; the caller falls back to the systems line.
public sealed class ExpansionException(string message) : Exception(message);

// Expands line and symbol text against a situation. Symbol options are picked by the seed key, never by an RNG stream.
public static class Expander
{
    public const int MaxDepth = 8;

    public static IReadOnlyList<TextPart> Expand(string text, Situation situation, IReadOnlyDictionary<string, SymbolDef> symbols, string seedKey)
    {
        var parts = new List<TextPart>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        Append(text, situation, symbols, seedKey, 0, parts, occurrences);
        return Merge(parts);
    }

    private static void Append(string text, Situation situation, IReadOnlyDictionary<string, SymbolDef> symbols, string seedKey, int depth, List<TextPart> parts, Dictionary<string, int> occurrences)
    {
        if (depth > MaxDepth)
        {
            throw new ExpansionException($"Symbols nest more than {MaxDepth} deep.");
        }

        IReadOnlyList<TextPiece> pieces;
        try
        {
            pieces = NarrativeText.Parse(text);
        }
        catch (NarrativeFormatException e)
        {
            throw new ExpansionException(e.Message);
        }

        foreach (TextPiece piece in pieces)
        {
            if (piece.Literal is { } literal)
            {
                parts.Add(new TextPart(false, literal));
                continue;
            }

            Token token = piece.Token!;
            int start = parts.Count;
            Resolve(token, situation, symbols, seedKey, depth, parts, occurrences);
            if (token.Capitalize)
            {
                Capitalize(parts, start);
            }
        }
    }

    private static void Resolve(Token token, Situation situation, IReadOnlyDictionary<string, SymbolDef> symbols, string seedKey, int depth, List<TextPart> parts, Dictionary<string, int> occurrences)
    {
        if (situation.People.TryGetValue(token.Head, out Person? person))
        {
            if (token.Singular is not null)
            {
                parts.Add(new TextPart(false, person.Pronouns.Plural ? token.Plural! : token.Singular));
            }
            else if (token.Form is { } form)
            {
                parts.Add(new TextPart(false, Pronoun(person.Pronouns, form)));
            }
            else
            {
                parts.Add(new TextPart(true, person.Name));
            }

            return;
        }

        if (token.Form is not null || token.Singular is not null)
        {
            throw new ExpansionException($"#{token.Raw}# needs a person, and this beat has no '{token.Head}'.");
        }

        if (situation.Words.TryGetValue(token.Head, out string? word))
        {
            parts.Add(new TextPart(true, word));
            return;
        }

        if (symbols.TryGetValue(token.Head, out SymbolDef? symbol) && symbol.Options.Count > 0)
        {
            int n = occurrences.GetValueOrDefault(symbol.Id);
            occurrences[symbol.Id] = n + 1;
            string option = symbol.Options[StableHash.Pick(string.Create(CultureInfo.InvariantCulture, $"{seedKey}|{symbol.Id}|{n}|{depth}"), symbol.Options.Count)];
            Append(option, situation, symbols, seedKey, depth + 1, parts, occurrences);
            return;
        }

        throw new ExpansionException($"#{token.Raw}# names nothing: no person, thing, or symbol is called '{token.Head}'.");
    }

    private static string Pronoun(PronounSet set, string form) => form switch
    {
        "they" => set.Subject,
        "them" => set.Object,
        "their" => set.Possessive,
        "theirs" => set.PossessivePronoun,
        "themself" => set.Reflexive,
        _ => throw new ExpansionException($"Unknown pronoun form '{form}'."),
    };

    // Upper-cases the first letter the token produced, in a name or in text.
    private static void Capitalize(List<TextPart> parts, int start)
    {
        for (int i = start; i < parts.Count; i++)
        {
            string text = parts[i].Text;
            int at = 0;
            while (at < text.Length && !char.IsLetter(text[at]))
            {
                at++;
            }

            if (at < text.Length)
            {
                parts[i] = parts[i] with { Text = string.Concat(text.AsSpan(0, at), char.ToUpperInvariant(text[at]).ToString(), text.AsSpan(at + 1)) };
                return;
            }
        }
    }

    // Adjacent text runs become one; names stay apart so each is encoded and kept from figure-wrapping on its own.
    private static List<TextPart> Merge(List<TextPart> parts)
    {
        var merged = new List<TextPart>();
        foreach (TextPart part in parts.Where(p => p.Text.Length > 0))
        {
            if (!part.IsName && merged.Count > 0 && !merged[^1].IsName)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + part.Text };
            }
            else
            {
                merged.Add(part);
            }
        }

        return merged;
    }
}

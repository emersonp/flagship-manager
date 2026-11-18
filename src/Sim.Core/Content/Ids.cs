using System.Text.RegularExpressions;

namespace Sim.Core.Content;

// The two id rules of docs/conventions.md, the only ones the loader and the editor apply.
public static partial class Ids
{
    public const int MaxLength = 40;

    public const string ItemRule = "Upper case: a letter, then letters, digits, and underscores, at most 40 in all.";

    public const string InnerRule = "Lower case: a letter, then letters, digits, and underscores, at most 40 in all.";

    // Skills, crew, gear, assignments, objectives, encounters, decisions, lines, and storylets: anything with its own page.
    public static bool IsItemId(string id)
    {
        return ItemPattern().IsMatch(id);
    }

    // Approaches, options, traits, symbols, pronoun sets, and tags: what lives inside an item or is typed into text.
    public static bool IsInnerId(string id)
    {
        return InnerPattern().IsMatch(id);
    }

    // \z, not $, so a trailing line break is not let through.
    [GeneratedRegex("^[A-Z][A-Z0-9_]{0,39}\\z")]
    private static partial Regex ItemPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}\\z")]
    private static partial Regex InnerPattern();
}

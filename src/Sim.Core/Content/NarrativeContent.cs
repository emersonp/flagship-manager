namespace Sim.Core.Content;

// A criterion holds when its fact equals one of Values (a set fact: contains one of them), or, for a range, lies within Min and Max.
public sealed record CriterionDef(string Fact, IReadOnlyList<string> Values, int? Min = null, int? Max = null)
{
    public bool IsRange => Values.Count == 0;
}

// Use is "log" (the live mission log) or "report" (the aftermath report). Beat is the beat kind, which counts as one criterion.
public sealed record LineDef(string Id, string Use, string Beat, IReadOnlyList<CriterionDef> When, string Text, string? Name = null)
{
    public int Specificity => When.Count + 1;
}

public sealed record SymbolDef(string Id, IReadOnlyList<string> Options, string? Name = null);

// Plural decides verb agreement: "they check", not "they checks".
public sealed record PronounSet(string Id, string Subject, string Object, string Possessive, string PossessivePronoun, string Reflexive, bool Plural, string? Name = null)
{
    // A crew member with no pronouns field uses the pack's "they" set, or this when the pack has none.
    public static readonly PronounSet They = new("they", "they", "them", "their", "theirs", "themself", true);
}

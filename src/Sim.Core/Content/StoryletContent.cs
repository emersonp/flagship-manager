namespace Sim.Core.Content;

// A requirement of a storylet: the flag's value lies in Min to Max (no limit when Max is null). A flag nobody has set is 0.
public sealed record FlagRange(string Flag, int Min, int? Max)
{
    public bool Holds(int value)
    {
        return value >= Min && (Max is null || value <= Max);
    }
}

// One effect of an answer: a loyalty change or a flag change. LoyaltyOn is "subject" or "crew" when Loyalty is not 0.
// Flag.On is "ship", "subject", or "" (the scope the storylet arrived for).
public sealed record StoryletEffect(int Loyalty = 0, string? LoyaltyOn = null, FlagChange? Flag = null);

public sealed record StoryletOption(string Id, string Label, IReadOnlyList<StoryletEffect> Effects);

// Scope is "ship" or "crew": whose flags the requirements read, and whether the storylet arrives for a subject.
public sealed record StoryletDef(string Id, int Stakes, string Scope, IReadOnlyList<FlagRange> Requires, IReadOnlyList<StoryletOption> Options, string? Name = null);

// The words a storylet's scope and its effects' on are written with.
public static class StoryletScopes
{
    public const string Ship = "ship";
    public const string Crew = "crew";

    // The crew member a storylet for crew arrived for.
    public const string Subject = "subject";
}

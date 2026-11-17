namespace Sim.Core.Content;

// One requirement of a storylet: a flag's value, a stat of the subject, or a fact about the last cycle (1e part two).
public abstract record Requirement;

// The flag's value lies in Min to Max (no limit when Max is null). A flag nobody has set is 0.
public sealed record FlagRange(string Flag, int Min, int? Max) : Requirement
{
    public bool Holds(int value)
    {
        return value >= Min && (Max is null || value <= Max);
    }
}

// The subject's stat (one of StoryletStats) lies in Min to Max, as a flag's range does.
public sealed record StatRange(string Stat, int Min, int? Max) : Requirement
{
    public bool Holds(int value)
    {
        return value >= Min && (Max is null || value <= Max);
    }
}

// A fact about the subject in the cycle whose aftermath last ended (one of LastCycleFacts); Not turns it round.
public sealed record LastCycle(string Fact, bool Not) : Requirement;

// One effect of an answer: a loyalty change, a morale change, or a flag change. LoyaltyOn and MoraleOn are "subject" or "crew"
// when their amount is not 0. Flag.On is "ship", "subject", or "" (the scope the storylet arrived for).
public sealed record StoryletEffect(int Loyalty = 0, string? LoyaltyOn = null, FlagChange? Flag = null, int Morale = 0, string? MoraleOn = null);

public sealed record StoryletOption(string Id, string Label, IReadOnlyList<StoryletEffect> Effects);

// Scope is "ship" or "crew": whose flags the requirements read, and whether the storylet arrives for a subject.
public sealed record StoryletDef(string Id, int Stakes, string Scope, IReadOnlyList<Requirement> Requires, IReadOnlyList<StoryletOption> Options, string? Name = null)
{
    // The requirements on flags, the only ones a storylet's own answers can move.
    public IEnumerable<FlagRange> Flags => Requires.OfType<FlagRange>();
}

// The words a storylet's scope and its effects' on are written with.
public static class StoryletScopes
{
    public const string Ship = "ship";
    public const string Crew = "crew";

    // The crew member a storylet for crew arrived for.
    public const string Subject = "subject";
}

// The stats a requirement can read on the subject.
public static class StoryletStats
{
    public const string Morale = "morale";
    public const string Loyalty = "loyalty";

    public static readonly IReadOnlyList<string> All = [Morale, Loyalty];
}

// The facts about the last cycle a requirement can read on the subject (design 4.2).
public static class LastCycleFacts
{
    // On the last mission and came home.
    public const string Sent = "sent";

    // Injured or incapacitated on it.
    public const string Hurt = "hurt";

    // Not sent, and fit.
    public const string Rested = "rested";

    // Someone died on it, and this one is alive.
    public const string LostCrewmate = "lost_crewmate";

    public static readonly IReadOnlyList<string> All = [Sent, Hurt, Rested, LostCrewmate];
}

// A vignette (1e part two, design 4.3): a short storylet with no stakes. Either Effects, applied when it arrives, or exactly two
// Options and a Default naming one of them, taken when the itinerary closes unanswered. Its words are lines of the storylet beats.
public sealed record VignetteDef(string Id, string Scope, IReadOnlyList<Requirement> Requires, IReadOnlyList<StoryletEffect> Effects,
    IReadOnlyList<StoryletOption> Options, string? Default, string? Name = null)
{
    // The first fact about the last cycle it reads, for its head ("lost a crewmate"); null when it reads none.
    public string? Fact => Requires.OfType<LastCycle>().FirstOrDefault(f => !f.Not)?.Fact;
}

namespace Sim.Core.Content;

// One requirement of a storylet: a trait's value, a stat of the subject, or a fact about the last cycle (1e part two).
public abstract record Requirement;

// The trait's value lies in Min to Max (no limit when Max is null). A trait nobody has set is 0.
public sealed record TraitRange(string Trait, int Min, int? Max) : Requirement
{
    public bool Holds(int value)
    {
        return value >= Min && (Max is null || value <= Max);
    }
}

// The subject's stat (one of StoryletStats) lies in Min to Max, as a trait's range does. OfTarget: the pair's target's (1e part three).
public sealed record StatRange(string Stat, int Min, int? Max, bool OfTarget = false) : Requirement
{
    public bool Holds(int value)
    {
        return value >= Min && (Max is null || value <= Max);
    }
}

// A fact about the subject in the cycle whose aftermath last ended (one of LastCycleFacts); Not turns it round. OfTarget: about
// the pair's target (1e part three).
public sealed record LastCycle(string Fact, bool Not, bool OfTarget = false) : Requirement;

// One effect of an answer: a loyalty change, a morale change, or a trait change. LoyaltyOn and MoraleOn are "subject", "target"
// (a pair's), or "crew" when their amount is not 0. Trait.On is "ship", "subject", "target", or "" (the scope the storylet arrived
// for, the subject for a pair); Trait.Toward is "target" for a pair trait the subject holds toward the target.
public sealed record StoryletEffect(int Loyalty = 0, string? LoyaltyOn = null, TraitChange? Trait = null, int Morale = 0, string? MoraleOn = null);

public sealed record StoryletOption(string Id, string Label, IReadOnlyList<StoryletEffect> Effects);

// Scope is "ship", "crew", or "pair": whose traits the requirements read, and whether the storylet arrives for a subject, or for a
// subject (the holder) and a target (1e part three).
public sealed record StoryletDef(string Id, int Stakes, string Scope, IReadOnlyList<Requirement> Requires, IReadOnlyList<StoryletOption> Options, string? Name = null)
{
    // The requirements on traits, the only ones a storylet's own answers can move.
    public IEnumerable<TraitRange> Traits => Requires.OfType<TraitRange>();
}

// The words a storylet's scope and its effects' on are written with.
public static class StoryletScopes
{
    public const string Ship = "ship";
    public const string Crew = "crew";

    // A holder and a target (1e part three): the storylet arrives once for each pair whose requirements hold.
    public const string Pair = "pair";

    // The crew member a storylet for crew arrived for; for a pair, the holder.
    public const string Subject = "subject";

    // The other crew member a storylet for a pair arrived for.
    public const string Target = "target";
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

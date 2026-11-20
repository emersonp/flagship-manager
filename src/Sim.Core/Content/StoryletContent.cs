namespace Sim.Core.Content;

// An expression of the storylet language (1e part four, design 4.3), true or false. Checked at load by Sim.Expressions, which also
// compiles it for the storylets module.
public sealed record Expr(string Source);

// One named crew member a storylet is cast with (design 4.2), and the expression that must hold on them. Slots are read in order:
// a slot's expression reads the slots before it and itself, never a later one.
public sealed record CastSlot(string Name, Expr Condition);

// One trait an expression reads: on a slot or "ship", and toward a slot for a pair trait. The lint reads these, and "where it came
// from" takes the first that is set.
public sealed record TraitRead(string Trait, string Slot, string? Toward);

// How long a storylet or vignette is held back after it arrives (1e part four, design 4.6), in cycles. Cast: the same binding (the
// same crew in the same slots) arrives again no sooner than this many cycles after its last arrival. Global: the storylet, with any
// cast, likewise. 0 holds nothing back.
public sealed record Cooldown(int Cast, int Global)
{
    public static readonly Cooldown None = new(0, 0);
}

// The weights of salience (tuning salience.*; design 4.5): stakes x Stakes + specificity x Specificity - Repeat x the arrivals in the
// last Window cycles, over every cast.
public sealed record SalienceTuning(int Stakes, int Specificity, int Repeat, int Window)
{
    public static readonly SalienceTuning Default = new(10, 1, 3, 20);
}

// One effect of an answer: a loyalty change, a morale change, or a trait change. LoyaltyOn and MoraleOn are a slot or "crew" (every
// living crew member) when their amount is not 0. Trait.On is "ship", a slot, or "" (the first slot; the ship for a storylet with
// no cast); Trait.Toward is a slot, for a pair trait held toward them (1e part four).
public sealed record StoryletEffect(int Loyalty = 0, string? LoyaltyOn = null, TraitChange? Trait = null, int Morale = 0, string? MoraleOn = null);

// chronicle: on a storylet (Weight only) or an option (Weight, Text, or both) (slice 1e part five, design 4.4 and 4.5). Weight: the
// weight of the entry its answer writes, 0 to 5, in place of the tuning file's; 0 writes none; null leaves it to the storylet, then
// the tuning file. Text: a line of the chronicle_storylet beat, with the storylet's cast as its people, written in place of the pack's.
public sealed record ChronicleDef(int? Weight, string? Text);

// Requires: shown, and accepted, only while it holds, with every slot bound. Check: settled by a check with a handler the captain
// names (1e part four, design 4.7); its effects are then in the check's outcomes, and its own Effects are empty.
public sealed record StoryletOption(string Id, string Label, IReadOnlyList<StoryletEffect> Effects, Expr? Requires = null, StoryletCheck? Check = null)
{
    // Each set of effects an answer with it may apply: its own, or its check's success and then failure.
    public IEnumerable<IReadOnlyList<StoryletEffect>> Outcomes => Check is null ? [Effects] : [Check.Success.Effects, Check.Failure.Effects];

    // Slice 1e part five: none unless written.
    public ChronicleDef? Chronicle { get; init; }
}

// An option settled by a check (design 4.7): the handler's Skill, plus their morale and trait check bonuses, plus the check dice,
// against Difficulty + 7. A margin of 0 or more is Success. In each outcome's effects and text the handler is the slot "handler".
// Its text is a line of the storyletAnswer beat, with the fact outcome, as an option's is.
public sealed record StoryletCheck(string Skill, int Difficulty, StoryletOutcome Success, StoryletOutcome Failure)
{
    // What the dice are added to, as an encounter's check: difficulty + 7.
    public const int Base = 7;

    public int Against => Difficulty + Base;
}

public sealed record StoryletOutcome(IReadOnlyList<StoryletEffect> Effects);

// A storylet (1e part four, design 4.2). Cast: up to three slots, read in order; none: a storylet for the ship. Requires: read once
// every slot is bound. Reads: every trait its expressions read, in reading order (the cast, then requires), as the loader found
// them; empty for a storylet built by hand. Needs: those of them the lowest values that let it arrive have above 0, so something
// must set them first.
public sealed record StoryletDef(string Id, int Stakes, IReadOnlyList<CastSlot> Cast, Expr? Requires, IReadOnlyList<StoryletOption> Options, string? Name = null)
{
    public IReadOnlyList<TraitRead> Reads { get; init; } = [];

    public IReadOnlyList<TraitRead> Needs { get; init; } = [];

    // 1e part four, Task 5: none unless written.
    public Cooldown Cooldown { get; init; } = Cooldown.None;

    // Slice 1e part five: none unless written; a storylet's has no text.
    public ChronicleDef? Chronicle { get; init; }

    public IEnumerable<string> Slots => Cast.Select(c => c.Name);

    // Every expression it reads, in reading order: each slot's, then requires.
    public IEnumerable<Expr> Expressions => Cast.Select(c => c.Condition).Concat(Requires is null ? [] : [Requires]);
}

// The words a storylet's effects and speakers are written with, beside its own slot names.
public static class StoryletScopes
{
    public const string Ship = "ship";
    public const string Crew = "crew";

    // The slot names the game's and the fixture's storylets use; any lower-case name that is not reserved will do.
    public const string Subject = "subject";
    public const string Target = "target";

    // A slot may not be called any of these (design 4.2): each already means something in an expression, an effect, or a script.
    public static readonly IReadOnlyList<string> Reserved = ["ship", "crew", "captain", "cycle", "storylet", "chronicle", "best", "handler"];

    // The crew member the captain sends on a check: a slot only in a check's success and failure (design 4.7).
    public const string Handler = "handler";

    public const int MaxSlots = 3;
}

// A vignette (1e part two, design 4.3; cast and expressions from part four): a short storylet with no stakes. Either Effects, applied
// when it arrives, or exactly two Options and a Default naming one of them, taken when the itinerary closes unanswered. Its words
// are lines of the storylet beats. Fact: the first history kind its first slot's expression reads with last(), not under a !, for
// its head ("lost a crewmate"), as the loader found it; null when it reads none.
public sealed record VignetteDef(string Id, IReadOnlyList<CastSlot> Cast, Expr? Requires, IReadOnlyList<StoryletEffect> Effects,
    IReadOnlyList<StoryletOption> Options, string? Default, string? Name = null)
{
    public IReadOnlyList<TraitRead> Reads { get; init; } = [];

    public IReadOnlyList<TraitRead> Needs { get; init; } = [];

    public string? Fact { get; init; }

    // As written; null takes { cast: vignettes.cooldown } (ContentPack.CooldownOf).
    public Cooldown? Cooldown { get; init; }

    public IEnumerable<string> Slots => Cast.Select(c => c.Name);

    public IEnumerable<Expr> Expressions => Cast.Select(c => c.Condition).Concat(Requires is null ? [] : [Requires]);
}

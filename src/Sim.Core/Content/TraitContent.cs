namespace Sim.Core.Content;

// A trait's definition in content/traits/ (slice 1e, part three, design 4.3): every rule a trait has, so no code names a trait.
// A trait with no definition behaves as a flag did: on the ship or a crew member, any value, added to or set, no rules.
// Levels: names for values (1 or more), resolved to numbers at load; a trait with levels is only ever set. Max: every change is
// clamped to 0 and this. Briefing: the chip beside the holder in the briefing. ExpiresWeeks: back to 0 after this many weeks unchanged.
public sealed record TraitDef(
    string Id,
    string? Name,
    bool Shown,
    string? On,
    IReadOnlyDictionary<int, string> Levels,
    int? Max,
    string? Briefing,
    TraitLaunch? Launch,
    TraitMission? Mission,
    TraitStep? Every,
    int? ExpiresWeeks,
    IReadOnlyList<TraitRuleEffect> OnTargetDied)
{
    public bool HasLevels => Levels.Count > 0;

    public bool OnPair => On == TraitPlaces.Pair;

    // The highest value it may hold: its max, else its highest level, else no cap.
    public int Cap => Max ?? (HasLevels ? Levels.Keys.Max() : int.MaxValue);

    public int Clamp(int value)
    {
        return Math.Clamp(value, 0, Cap);
    }

    // Said wherever an add to a trait with levels is refused.
    public string LevelsAreSet => $"{Id} has named levels, so it is set, not added to.";

    // The level's name, or null when the value has none.
    public string? LevelName(int value)
    {
        return Levels.GetValueOrDefault(value);
    }

    // A definition with no rules, for a trait the pack never defines.
    public static TraitDef Plain(string id)
    {
        return new TraitDef(id, null, true, null, new Dictionary<int, string>(), null, null, null, null, null, null, []);
    }
}

// Where a trait may sit (a definition's on); a trait with no on sits on the ship or a crew member, never on a pair.
public static class TraitPlaces
{
    public const string Ship = "ship";
    public const string Crew = "crew";
    public const string Pair = "pair";

    public static readonly IReadOnlyList<string> All = [Ship, Crew, Pair];
}

// Every Weeks weeks after the trait last changed, it changes by Add (never 0), and the clock restarts.
public sealed record TraitStep(int Weeks, int Add);

// Rules that fire when a mission starts: Sent for a holder on the team; NotSent for a living holder not on it; WithTarget for a
// pair trait whose holder and target are both on it.
public sealed record TraitLaunch(IReadOnlyList<TraitRuleEffect> Sent, IReadOnlyList<TraitRuleEffect> NotSent, IReadOnlyList<TraitRuleEffect> WithTarget);

// CheckBonus on every check the holder makes, while the trait holds at launch; WithTarget: only when a pair's target is on the team too.
public sealed record TraitMission(int CheckBonus, bool WithTarget);

// One effect of a rule: a trait change (Trait null: the rule's own trait), a loyalty change, or a morale change, exactly one of them.
// On is "holder", "target" (a pair's), or "crew" (every living crew member; loyalty and morale only).
public sealed record TraitRuleEffect(string? Trait, int Amount, bool Set, int Loyalty, int Morale, string On)
{
    public bool ChangesTrait => Loyalty == 0 && Morale == 0;
}

// The words a rule's effects are written on.
public static class TraitRuleOn
{
    public const string Holder = "holder";
    public const string Target = "target";
    public const string Crew = "crew";
}

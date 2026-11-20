using Sim.Core.Content;

namespace Sim.Core;

// Contracts of the traits module (slice 1e, part three), here so the storylets module, the bridge, and the simulator never
// reference the module itself. A trait is a whole number of 0 or more on the ship (CrewId null), on one crew member, or on a
// pair (TargetId: the crew member it points at). 0 means absent.

// A change made in play: Set false adds Amount. Cause is one of TraitCauses. EncounterId: the encounter whose effect made it.
public sealed record ChangeTrait(string Trait, string? CrewId, string? TargetId, int Amount, bool Set, string Cause, string? EncounterId = null) : ICommand;

// Dev Play's direct change to one trait. Nothing in play sends it; the web sends it only from the developer routes, which are
// where any permission check belongs.
public sealed record SetTrait(string Trait, string? CrewId, string? TargetId, int Value) : ICommand
{
    public const int Most = 99;
}

// Published before the TraitChanged it causes, so the log tells a developer's change from one made in play.
[EventType("TraitSetByDeveloper", 1)]
public sealed record TraitSetByDeveloper(string Trait, string? CrewId, string? TargetId, int Value) : IEvent;

// Replaces FlagChanged (GDD 7.2: a changed meaning is a new event type). EncounterId: the encounter whose effect made the change.
[EventType("TraitChanged", 1)]
public sealed record TraitChanged(string Trait, string? CrewId, string? TargetId, int From, int To, string Cause, string? EncounterId) : IEvent;

// What made a trait change (design 5.3).
public static class TraitCauses
{
    // An encounter's effect.
    public const string Mission = "mission";

    // A storylet's answer.
    public const string Answer = "answer";

    public const string Vignette = "vignette";

    // A definition's launch rule.
    public const string Launch = "launch";

    // A definition's weekly step.
    public const string Weekly = "weekly";

    public const string Expired = "expired";

    // A pair's onTargetDied, or a holder's or target's death dropping it.
    public const string Death = "death";

    public const string Developer = "developer";
}

// One launch rule that could fire (design 4.7): the holder's trait (a pair's target too), when it fires, and what it does.
// When is one of LaunchWhen.
public sealed record LaunchPreview(string Trait, string HolderId, string? TargetId, string When, IReadOnlyList<TraitRuleEffect> Effects)
{
    // Whether a mission with this team fires it. NotSent waits for a mission: a cycle with nobody sent fires nothing.
    public bool Fires(IReadOnlyCollection<string> team)
    {
        return When switch
        {
            LaunchWhen.Sent => team.Contains(HolderId),
            LaunchWhen.NotSent => team.Count > 0 && !team.Contains(HolderId),
            _ => team.Contains(HolderId) && TargetId is not null && team.Contains(TargetId),
        };
    }
}

public static class LaunchWhen
{
    public const string Sent = "sent";
    public const string NotSent = "notSent";
    public const string WithTarget = "withTarget";
}

// One trait as it stands. TargetId: the crew member a pair trait points at. EncounterId: the last encounter that changed it,
// or null when nothing on a mission has.
public sealed record TraitHeld(string Trait, string? TargetId, int Value, int ChangedAtDay, string? EncounterId);

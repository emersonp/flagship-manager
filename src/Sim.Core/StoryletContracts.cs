namespace Sim.Core;

// Contracts of the storylets module, here so the bridge and the simulator need not reference the module's internals.
public sealed record AnswerStorylet(string OfferId, string OptionId) : ICommand
{
    // Why the aftermath cannot end: said by the guard and by the screen that shows the same block.
    public const string WaitingReason = "A storylet is waiting for an answer.";
}

// Handled by the crew module, which clamps the result to its tuning, so the storylets module never touches crew state.
public sealed record AdjustLoyalty(string CrewId, int Delta) : ICommand;

// Dev Play's direct change to one flag: CrewId null is the ship. Nothing in play sends it; the web sends it only from the
// developer routes, which are where any permission check belongs.
public sealed record SetFlag(string Flag, string? CrewId, int Value) : ICommand
{
    public const int Most = 99;
}

// Published before the FlagChanged it causes, so the log tells a developer's change from one made in play.
[EventType("FlagSetByDeveloper", 1)]
public sealed record FlagSetByDeveloper(string Flag, string? CrewId, int Value) : IEvent;

[EventType("LoyaltyChanged", 1)]
public sealed record LoyaltyChanged(string CrewId, int From, int To) : IEvent;

// CrewId null: the ship. EncounterId: the encounter whose effect made the change, or null for an answer.
[EventType("FlagChanged", 1)]
public sealed record FlagChanged(string Flag, string? CrewId, int From, int To, string? EncounterId) : IEvent;

// SubjectId null: a storylet for the ship. EncounterId: where the flag that brought it was last changed by a mission, if anywhere.
[EventType("StoryletOffered", 1)]
public sealed record StoryletOffered(string OfferId, string StoryletId, string? SubjectId, string? EncounterId) : IEvent;

[EventType("StoryletAnswered", 1)]
public sealed record StoryletAnswered(string OfferId, string StoryletId, string? SubjectId, string OptionId) : IEvent;

[EventType("StoryletWithdrawn", 1)]
public sealed record StoryletWithdrawn(string OfferId, string StoryletId, string? SubjectId) : IEvent;

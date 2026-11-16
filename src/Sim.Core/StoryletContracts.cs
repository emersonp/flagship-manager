namespace Sim.Core;

// Contracts of the storylets module, here so the bridge and the simulator need not reference the module's internals.
public sealed record AnswerStorylet(string OfferId, string OptionId) : ICommand;

// Handled by the crew module, which clamps the result to its tuning, so the storylets module never touches crew state.
public sealed record AdjustLoyalty(string CrewId, int Delta) : ICommand;

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

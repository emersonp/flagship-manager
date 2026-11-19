namespace Sim.Core;

// Contracts of the storylets module, here so the bridge and the simulator need not reference the module's internals.
public sealed record AnswerStorylet(string OfferId, string OptionId) : ICommand
{
    // Why the aftermath cannot end: said by the guard and by the screen that shows the same block.
    public const string WaitingReason = "A storylet is waiting for an answer.";
}

// Handled by the crew module, which clamps the result to its tuning, so the storylets and traits modules never touch crew state.
public sealed record AdjustLoyalty(string CrewId, int Delta) : ICommand;

[EventType("LoyaltyChanged", 1)]
public sealed record LoyaltyChanged(string CrewId, int From, int To) : IEvent;

// 1e part four (design 5.2): events carry the cast, each slot to a crew member's id in reading order; empty for the ship. A changed
// meaning, so new versions (GDD 7.2). OfferId is {cycle}:{id}: a storylet arrives at most once a cycle. EncounterId: where the
// first trait its expressions read that is set was last changed by a mission, if anywhere. Salience: its score when it arrived
// (design 4.5).
[EventType("StoryletOffered", 2)]
public sealed record StoryletOffered(string OfferId, string StoryletId, IReadOnlyDictionary<string, string> Cast, string? EncounterId, int Salience) : IEvent;

[EventType("StoryletAnswered", 2)]
public sealed record StoryletAnswered(string OfferId, string StoryletId, IReadOnlyDictionary<string, string> Cast, string OptionId) : IEvent;

// A cast member died, or the pack lost the storylet, while it waited.
[EventType("StoryletWithdrawn", 2)]
public sealed record StoryletWithdrawn(string OfferId, string StoryletId, IReadOnlyDictionary<string, string> Cast) : IEvent;

// 1e part two: vignettes. Answered only while the itinerary is open; one left unanswered takes its default when it closes.
public sealed record AnswerVignette(string OfferId, string OptionId) : ICommand;

// Cast: as a storylet's; empty for the ship. Salience: as a storylet's, with no stakes.
[EventType("VignetteArrived", 2)]
public sealed record VignetteArrived(string OfferId, string VignetteId, IReadOnlyDictionary<string, string> Cast, int Salience) : IEvent;

// Published after the changes it made, as StoryletAnswered is. OptionId null: a vignette with no choice, whose effects applied
// as it arrived. Defaulted: the itinerary closed without an answer.
[EventType("VignetteAnswered", 2)]
public sealed record VignetteAnswered(string OfferId, string VignetteId, IReadOnlyDictionary<string, string> Cast, string? OptionId, bool Defaulted) : IEvent;

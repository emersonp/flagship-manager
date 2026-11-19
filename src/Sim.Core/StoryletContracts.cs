using System.Text.Json.Serialization;
using Sim.Core.Content;

namespace Sim.Core;

// Contracts of the storylets module, here so the bridge and the simulator need not reference the module's internals.
// HandlerId: who the captain sends, for an option settled by a check (1e part four, design 4.7); required for one, refused on any
// other. The answer is one command, so a handler cannot die between being named and the roll.
public sealed record AnswerStorylet(string OfferId, string OptionId, string? HandlerId = null) : ICommand
{
    // Why the aftermath cannot end: said by the guard and by the screen that shows the same block.
    public const string WaitingReason = "A storylet is waiting for an answer.";

    // Why a check option cannot be picked: the panel says it beside the disabled button (review focus 4).
    public const string NoOneFit = "No one fit to send.";
}

// The answers of a waiting storylet the captain may pick now, served by the storylets module so the bridge and the simulator read
// what its guard reads (1e part four, design 4.7 and 4.9).
public interface IStoryletView
{
    // The storylet's options whose requires holds, in the pack's order; empty for an offer not waiting or a storylet the pack lost.
    IReadOnlyList<OpenAnswer> Answers(string offerId);
}

// Handlers: null for an option with no check; else everyone who may be sent, in roster order, which may be none.
public sealed record OpenAnswer(string OptionId, IReadOnlyList<HandlerOdds>? Handlers);

// A crew member who may handle a check: their skill level, the whole of what is added to the dice (skill, morale, traits), what the
// total must reach, and the chance that it does.
public sealed record HandlerOdds(string CrewId, int Skill, int Score, int Against, Chance Chance);

// How a check went (design 4.7): the skill and its level, the morale and trait check bonuses, the dice, and what the total had to
// reach. Score is the level and bonuses; the margin is Score + Dice - Against, and 0 or more is Success.
public sealed record StoryletRoll(string Skill, int Level, int Morale, IReadOnlyList<TraitBonus> Traits, int Dice, int Against, bool Success)
{
    [JsonIgnore]
    public int Score => Level + Morale + Traits.Sum(t => t.Amount);
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

// HandlerId and Roll: who was sent and how the check went, for an option settled by one (Task 6, inside version 2); the handler is
// not part of the cast.
[EventType("StoryletAnswered", 2)]
public sealed record StoryletAnswered(string OfferId, string StoryletId, IReadOnlyDictionary<string, string> Cast, string OptionId, string? HandlerId = null, StoryletRoll? Roll = null) : IEvent;

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

// What became of a storylet or vignette when an itinerary opened (1e part four, design 4.4 to 4.6). Arrived and LostOnBudget are
// the contenders (design 4.1); the others say why it was not one. Cooling: in its global cooldown. CastCooling: every cast that held
// is in its cast cooldown (Task 7; until then it read as NoCast).
public enum ContestFate
{
    Arrived,
    LostOnBudget,
    Waiting,
    Cooling,
    CastCooling,
    NoCast,
    OutOfTries,
}

// Salience in parts (design 4.5), each already weighted by its tuning: stakes, specificity, and freshness, which is taken off. Tie:
// its place in the draw from salience:<cycle>, 1 first, which orders only equal totals.
public sealed record SalienceScore(int Stakes, int Specificity, int Freshness, int Tie)
{
    public int Total => Stakes + Specificity - Freshness;
}

// One storylet or vignette in a contest. Cast and Score: for a contender; for one whose every cast that held is cooling, Cast is the
// first such cast. ReturnsAt: for one cooling, the first cycle it (or that cast) may arrive again. Tries: the expressions its
// casting read. Eligible (design 4.10): out of its global cooldown and not waiting, with a first slot that held for someone (for
// the ship, a requires that held).
public sealed record Contender(string Id, ContestFate Fate, IReadOnlyDictionary<string, string>? Cast, SalienceScore? Score, int? ReturnsAt, int Tries)
{
    public bool Eligible { get; init; }
}

// Each list holds the contenders in salience order, the arrivals first, then the rest in the pack's order. Cycle: the itinerary it
// is for; Open: that itinerary is open now, rather than the next one, foreseen between cycles (Dev Play's dry run).
public sealed record Contest(IReadOnlyList<Contender> Storylets, IReadOnlyList<Contender> Vignettes)
{
    public int Cycle { get; init; }

    public bool Open { get; init; }
}

// The first top-level clause that held for no one (design 4.10): on a slot, or on requires. Before: how many crew (or casts, past
// the first slot) the clauses before it in the same expression held for; null for its first clause. Clause null: the slot had too
// many casts to search.
public sealed record FailedClause(string Slot, string? Clause, int? Before)
{
    public const string Requires = "requires";
}

// Dev Play's "Why these storylets" (1e part four, design 4.10), served by the storylets module: a dry run of the same function the
// module runs when an itinerary opens, on copies of its streams, which advances no stream and changes nothing (review focus 3).
// In the itinerary, it is the contest as it ran when it opened: cycleEvents (this cycle's) say what has happened since, and it is
// read as it was before. Between cycles it is the next itinerary's, as things stand now.
public interface IStoryletDryRun
{
    Contest Contest(IReadOnlyList<IEvent> cycleEvents);

    // For one that did not cast, the first clause that held for no one, read over every crew member and cast in roster order;
    // null when every clause held for someone, or for an id the pack lacks.
    FailedClause? Failed(string id, IReadOnlyList<IEvent> cycleEvents);
}

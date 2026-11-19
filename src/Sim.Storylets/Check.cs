using Sim.Core;
using Sim.Core.Content;

namespace Sim.Storylets;

// An option settled by a check (1e part four, design 4.7): who may be sent, what they bring, and the roll.
internal static class Check
{
    // Anyone living (on the roster, so aboard while the itinerary is open), fit, and not in the cast, in roster order.
    public static IEnumerable<string> Handlers(IRosterView roster, IReadOnlyDictionary<string, string> cast)
    {
        return roster.Roster.Where(id => roster.IsFit(id) && !cast.Values.Contains(id));
    }

    // Why this crew member cannot be sent, or null when they can.
    public static string? Refusal(IRosterView roster, IReadOnlyDictionary<string, string> cast, string crewId)
    {
        if (!roster.Roster.Contains(crewId))
        {
            return $"Crew member '{crewId}' is not on the roster.";
        }

        if (cast.Values.Contains(crewId))
        {
            return $"Crew member '{crewId}' is in this storylet's cast, so cannot be sent.";
        }

        return roster.IsFit(crewId) ? null : $"Crew member '{crewId}' is not fit to send.";
    }

    // What the handler adds to the dice: their skill level, their morale's modifier (as on a mission), and their traits' check
    // bonuses with the team read as the cast and the handler, so a pair's withTarget bonus counts toward a cast member.
    public static (int Level, int Morale, IReadOnlyList<TraitBonus> Traits) Score(StoryletCheck check, string handlerId, IReadOnlyDictionary<string, string> cast, IRosterView roster, ITraitView traits)
    {
        TeamMember member = roster.Member(handlerId)!;
        List<string> team = [.. cast.Values, handlerId];
        return (member.Skills.GetValueOrDefault(check.Skill), member.MoraleBonus, traits.Bonuses(handlerId, team));
    }

    public static HandlerOdds Odds(StoryletCheck check, string handlerId, IReadOnlyDictionary<string, string> cast, IRosterView roster, ITraitView traits, DiceDef dice)
    {
        (int level, int morale, IReadOnlyList<TraitBonus> bonuses) = Score(check, handlerId, cast, roster, traits);
        int score = level + morale + bonuses.Sum(b => b.Amount);
        return new HandlerOdds(handlerId, level, score, check.Against, Chances.Of(dice, score, check.Against));
    }

    // The dice from the offer's own stream, so the same offer rolls the same whoever is sent. A margin of 0 or more is success.
    public static StoryletRoll Roll(StoryletCheck check, string handlerId, IReadOnlyDictionary<string, string> cast, IRosterView roster, ITraitView traits, DiceDef dice, RngStream stream)
    {
        (int level, int morale, IReadOnlyList<TraitBonus> bonuses) = Score(check, handlerId, cast, roster, traits);
        int rolled = dice.Roll(stream);
        int margin = level + morale + bonuses.Sum(b => b.Amount) + rolled - check.Against;
        return new StoryletRoll(check.Skill, level, morale, bonuses, rolled, check.Against, margin >= 0);
    }

    public static string Stream(string offerId)
    {
        return $"storylet-check:{offerId}";
    }
}

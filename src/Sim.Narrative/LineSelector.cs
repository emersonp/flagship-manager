using System.Globalization;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Sim.Narrative;

// Salience selection (GDD section 6): of the lines whose criteria all hold, the one with the most criteria wins.
public static class LineSelector
{
    // Every line that holds for the situation, most specific first, then by id.
    public static IReadOnlyList<LineDef> Candidates(IReadOnlyList<LineDef> lines, Situation situation)
    {
        return lines
            .Where(l => l.Beat == situation.Beat && l.When.All(c => Holds(c, situation)))
            .OrderByDescending(l => l.Specificity)
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .ToList();
    }

    // The winner, or null when nothing holds. Among the most specific, a line not yet used in this text wins over one that was;
    // the rest is a tie, broken by the seed key, so the same situation always gets the same line.
    public static LineDef? Select(IReadOnlyList<LineDef> lines, Situation situation, IReadOnlySet<string> used, string seedKey)
    {
        IReadOnlyList<LineDef> candidates = Candidates(lines, situation);
        if (candidates.Count == 0)
        {
            return null;
        }

        int best = candidates[0].Specificity;
        List<LineDef> top = candidates.TakeWhile(l => l.Specificity == best).ToList();
        List<LineDef> fresh = top.Where(l => !used.Contains(l.Id)).ToList();
        List<LineDef> pool = fresh.Count > 0 ? fresh : top;
        return pool[StableHash.Pick(seedKey + "|line", pool.Count)];
    }

    // An absent fact never holds, so a line about a helper never fires when there is none.
    public static bool Holds(CriterionDef criterion, Situation situation)
    {
        if (!situation.Facts.TryGetValue(criterion.Fact, out FactValue? fact))
        {
            return false;
        }

        if (criterion.IsRange)
        {
            return fact.Type == FactType.Number && (criterion.Min is not { } min || fact.Number >= min) && (criterion.Max is not { } max || fact.Number <= max);
        }

        return fact.Type switch
        {
            FactType.Text => criterion.Values.Contains(fact.Text!, StringComparer.Ordinal),
            FactType.Number => criterion.Values.Contains(fact.Number.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal),
            FactType.Flag => criterion.Values.Contains(fact.Flag ? "true" : "false", StringComparer.Ordinal),
            FactType.Set => criterion.Values.Any(v => fact.Set!.Contains(v, StringComparer.Ordinal)),
            _ => false,
        };
    }
}

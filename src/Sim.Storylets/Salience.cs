using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;

namespace Sim.Storylets;

// Review focus 3: the one function that decides what arrives when the itinerary opens. It reads the state and changes nothing, so
// the module runs it and then applies the arrivals, and Dev Play's dry run (Task 7) reads the same contest with copies of the
// streams. Streams are asked for by name: cast:<id>:<cycle> for each one cast, then salience:<cycle> once, for the ties, storylets'
// before vignettes'.
internal static class Salience
{
    public static Contest Contend(ContentPack pack, Casting casting, StoryletsState state, IStateSource source, IReadOnlyList<string> roster, int cycle, Func<string, RngStream> rng)
    {
        Contender Enter(string id, int stakes, Cooldown cooldown, bool waiting)
        {
            if (waiting)
            {
                return new Contender(id, ContestFate.Waiting, null, null, null, 0);
            }

            // The global cooldown is read before casting, so a storylet in it is never cast and draws nothing.
            Fired? fired = state.Fired.FirstOrDefault(f => f.Id == id);
            if (fired is not null && cycle - fired.Last < cooldown.Global)
            {
                return new Contender(id, ContestFate.Cooling, null, null, fired.Last + cooldown.Global, 0);
            }

            // The cast cooldown is inside the search: a binding that arrived within it is passed over for the next.
            bool Cooling(IReadOnlyDictionary<string, string> cast)
            {
                return state.Arrivals.Any(a => a.Id == id && Same(a.Cast, cast) && cycle - a.Cycle < cooldown.Cast);
            }

            CastResult result = casting.Cast(id, source, roster, () => rng(CastStream(id, cycle)), pack.CastTries, Cooling);
            if (result.Cast is not { } bound)
            {
                // Every cast that held is cooling (Task 7): the first of them, and when it may arrive again.
                if (result is { Outcome: CastOutcome.Cooling, Held: { } held })
                {
                    int last = state.Arrivals.First(a => a.Id == id && Same(a.Cast, held)).Cycle;
                    return new Contender(id, ContestFate.CastCooling, held, null, last + cooldown.Cast, result.Tries) { Eligible = true };
                }

                ContestFate fate = result.Outcome == CastOutcome.OutOfTries ? ContestFate.OutOfTries : ContestFate.NoCast;
                return new Contender(id, fate, null, null, null, result.Tries) { Eligible = result.FirstHeld };
            }

            SalienceTuning weights = pack.Salience;
            int fires = fired?.Cycles.Count(c => c < cycle && cycle - c <= weights.Window) ?? 0;
            var score = new SalienceScore(stakes * weights.Stakes, casting.Specificity(id) * weights.Specificity, fires * weights.Repeat, 0);
            return new Contender(id, ContestFate.LostOnBudget, bound, score, null, result.Tries) { Eligible = true };
        }

        List<Contender> storylets = pack.Storylets.Select(s => Enter(s.Id, s.Stakes, s.Cooldown, state.Waiting.Any(w => w.StoryletId == s.Id))).ToList();
        List<Contender> vignettes = pack.Vignettes.Select(v => Enter(v.Id, 0, pack.CooldownOf(v), waiting: false)).ToList();

        RngStream? ties = null;
        RngStream Ties()
        {
            return ties ??= rng(SalienceStream(cycle));
        }

        int budget = Math.Max(0, pack.StoryletsPerCycle - state.Waiting.Count);
        return new Contest(Rank(storylets, budget, Ties), Rank(vignettes, pack.VignettesPerCycle, Ties));
    }

    // Design 5.4: casting draws from its own stream, by storylet or vignette and cycle.
    public static string CastStream(string id, int cycle)
    {
        return $"cast:{id}:{cycle}";
    }

    public static string SalienceStream(int cycle)
    {
        return $"salience:{cycle}";
    }

    // The same binding: the same crew in the same slots.
    public static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        return a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out string? id) && id == p.Value);
    }

    // Highest salience first, ties by the draw, up to the budget; the draw is a seeded order of the contenders in the pack's order
    // (Fisher-Yates from the end), taken only when there are two or more.
    private static List<Contender> Rank(List<Contender> entries, int budget, Func<RngStream> ties)
    {
        List<Contender> contenders = entries.Where(e => e.Score is not null).ToList();
        int[] places = [.. Enumerable.Range(1, contenders.Count)];
        if (contenders.Count > 1)
        {
            RngStream rng = ties();
            for (int i = places.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (places[i], places[j]) = (places[j], places[i]);
            }
        }

        IEnumerable<Contender> ranked = contenders
            .Select((c, k) => c with { Score = c.Score! with { Tie = places[k] } })
            .OrderByDescending(c => c.Score!.Total)
            .ThenBy(c => c.Score!.Tie)
            .Select((c, k) => c with { Fate = k < budget ? ContestFate.Arrived : ContestFate.LostOnBudget });
        return [.. ranked, .. entries.Where(e => e.Score is null)];
    }
}

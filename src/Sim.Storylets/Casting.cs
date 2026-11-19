using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;

namespace Sim.Storylets;

// Why a storylet or vignette did or did not cast this cycle (design 4.4). NoCast and OutOfTries are told apart for Dev Play and the
// simulator: the first means no binding held, the second that the search stopped before it knew.
internal enum CastOutcome
{
    Cast,
    NoCast,
    OutOfTries,
}

// Cast: each slot to a crew member's id, in reading order; empty for the ship. Null unless the outcome is Cast.
internal sealed record CastResult(CastOutcome Outcome, Dictionary<string, string>? Cast, int Tries);

// Every storylet's and vignette's expressions, checked and kept for the life of the module, and the search that casts them
// (1e part four, design 4.4).
internal sealed class Casting
{
    private readonly ExpressionBook book;
    private readonly Dictionary<string, Compiled> byId = new(StringComparer.Ordinal);

    public Casting(ContentPack pack)
    {
        book = new ExpressionBook(pack.Traits, pack.Skills.Select(s => s.Id), HistoryKinds.All, pack.HistoryCycles);
        foreach (StoryletDef storylet in pack.Storylets)
        {
            byId[storylet.Id] = Compile(storylet.Id, storylet.Cast, storylet.Requires);
        }

        foreach (VignetteDef vignette in pack.Vignettes)
        {
            byId[vignette.Id] = Compile(vignette.Id, vignette.Cast, vignette.Requires);
        }
    }

    // Every trait its expressions read, in reading order, each once: for "where it came from".
    public IReadOnlyList<TraitRead> Reads(string id)
    {
        return byId[id].Reads;
    }

    // The top-level && clauses across every slot's expression and requires (design 4.5), counted once, when the pack is loaded into
    // the module.
    public int Specificity(string id)
    {
        return byId[id].Specificity;
    }

    // A seeded order of the living crew from the stream, then depth first: each slot from those in that order, not already bound,
    // whose expression holds; then requires. Every expression read is a try. The first full binding that holds and is not cooling
    // is the cast. With no slots, requires is read once on the ship.
    public CastResult Cast(string id, IStateSource state, IReadOnlyList<string> roster, Func<RngStream> stream, int maxTries, Func<IReadOnlyDictionary<string, string>, bool> cooling)
    {
        Compiled compiled = byId[id];
        int tries = 0;
        bool outOfTries = false;
        var bound = new Dictionary<string, string>(StringComparer.Ordinal);

        bool Read(CheckedExpression expression)
        {
            if (tries >= maxTries)
            {
                outOfTries = true;
                return false;
            }

            tries++;
            return book.Evaluate(expression, state, bound);
        }

        if (compiled.Slots.Count == 0)
        {
            bool holds = (compiled.Requires is null || Read(compiled.Requires)) && !cooling(bound);
            return new CastResult(holds ? CastOutcome.Cast : outOfTries ? CastOutcome.OutOfTries : CastOutcome.NoCast, holds ? [] : null, tries);
        }

        List<string> order = Shuffled(roster, stream());

        bool Fill(int k)
        {
            if (k == compiled.Slots.Count)
            {
                return (compiled.Requires is null || Read(compiled.Requires)) && !cooling(bound);
            }

            (string slot, CheckedExpression condition) = compiled.Slots[k];
            foreach (string crewId in order)
            {
                // No slot is bound twice to the same crew member (design 4.2).
                if (bound.ContainsValue(crewId))
                {
                    continue;
                }

                bound[slot] = crewId;
                if (Read(condition) && Fill(k + 1))
                {
                    return true;
                }

                bound.Remove(slot);
                if (outOfTries)
                {
                    return false;
                }
            }

            return false;
        }

        if (Fill(0))
        {
            return new CastResult(CastOutcome.Cast, compiled.Slots.ToDictionary(s => s.Slot, s => bound[s.Slot], StringComparer.Ordinal), tries);
        }

        return new CastResult(outOfTries ? CastOutcome.OutOfTries : CastOutcome.NoCast, null, tries);
    }

    // Fisher-Yates, from the end.
    private static List<string> Shuffled(IReadOnlyList<string> roster, RngStream rng)
    {
        List<string> order = [.. roster];
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    // The loader has checked every expression already; one that fails here is a pack built by hand, and says so.
    private Compiled Compile(string id, IReadOnlyList<CastSlot> cast, Expr? requires)
    {
        List<string> slots = cast.Select(c => c.Name).ToList();
        CheckedExpression Check(string source, IReadOnlyList<string> readable, string key)
        {
            return book.Check(source, readable, out ExpressionError? error)
                ?? throw new InvalidOperationException($"{id}: {key}, at character {error!.Position + 1}: {error.Message}");
        }

        var compiled = cast.Select((c, k) => (c.Name, Check(c.Condition.Source, slots[..(k + 1)], $"cast.{c.Name}"))).ToList();
        CheckedExpression? all = requires is null ? null : Check(requires.Source, slots, "requires");
        List<CheckedExpression> every = [.. compiled.Select(c => c.Item2), .. all is null ? Array.Empty<CheckedExpression>() : [all]];
        return new Compiled(compiled, all, every.SelectMany(c => c.Traits).Distinct().ToList(), every.Sum(c => c.Clauses));
    }

    private sealed record Compiled(List<(string Slot, CheckedExpression Condition)> Slots, CheckedExpression? Requires, List<TraitRead> Reads, int Specificity);
}

// What an expression reads, over the views the module may use (design 4.3). Aboard: alive and not away; in Phase 1 nobody is away
// while the itinerary is open, so it is everyone living.
internal sealed class StateSource(IRosterView roster, ITraitView traits, StoryletsState state, int cycle) : IStateSource
{
    public int Cycle => cycle;

    public int Morale(string crewId)
    {
        return roster.Morale(crewId);
    }

    public int Loyalty(string crewId)
    {
        return roster.Loyalty(crewId);
    }

    public int Skill(string crewId, string skill)
    {
        return roster.Member(crewId)?.Skills.GetValueOrDefault(skill) ?? 0;
    }

    public bool IsFit(string crewId)
    {
        return roster.IsFit(crewId);
    }

    public bool IsAboard(string crewId)
    {
        return roster.Roster.Contains(crewId);
    }

    public int Trait(string trait, string? crewId, string? targetId = null)
    {
        return traits.Value(trait, crewId, targetId);
    }

    public int History(string crewId, string kind, int cycles)
    {
        return new HistoryView(state, cycle).Count(crewId, kind, cycles);
    }
}

using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;

namespace Sim.Storylets;

// Why a storylet or vignette did or did not cast this cycle (design 4.4). NoCast and OutOfTries are told apart for Dev Play and the
// simulator: the first means no binding held, the second that the search stopped before it knew. Cooling (Task 7): bindings held,
// and every one was in its cast cooldown.
internal enum CastOutcome
{
    Cast,
    NoCast,
    OutOfTries,
    Cooling,
}

// Cast: each slot to a crew member's id, in reading order; empty for the ship. Null unless the outcome is Cast. Held: for Cooling,
// the first binding that held. FirstHeld: the first slot's expression held for someone (for the ship, requires held), which is
// half of what Dev Play calls eligible (design 4.10).
internal sealed record CastResult(CastOutcome Outcome, Dictionary<string, string>? Cast, int Tries)
{
    public Dictionary<string, string>? Held { get; init; }

    public bool FirstHeld { get; init; }
}

// Every storylet's and vignette's expressions, checked and kept for the life of the module, and the search that casts them
// (1e part four, design 4.4).
internal sealed class Casting
{
    private readonly ExpressionBook book;
    private readonly Dictionary<string, Compiled> byId = new(StringComparer.Ordinal);

    // Each storylet option's requires, by storylet id and option id (Task 6).
    private readonly Dictionary<(string, string), CheckedExpression> optionRequires = [];

    public Casting(ContentPack pack)
    {
        book = new ExpressionBook(pack.Traits, pack.Skills.Select(s => s.Id), HistoryKinds.All, pack.HistoryCycles);
        foreach (StoryletDef storylet in pack.Storylets)
        {
            byId[storylet.Id] = Compile(storylet.Id, storylet.Cast, storylet.Requires);
            List<string> slots = storylet.Slots.ToList();
            foreach (StoryletOption option in storylet.Options.Where(o => o.Requires is not null))
            {
                optionRequires[(storylet.Id, option.Id)] = Checked(storylet.Id, option.Requires!.Source, slots, $"options.{option.Id}.requires");
            }
        }

        foreach (VignetteDef vignette in pack.Vignettes)
        {
            byId[vignette.Id] = Compile(vignette.Id, vignette.Cast, vignette.Requires);
        }
    }

    // Past this many casts for one slot, Dev Play's search for the failing clause gives up on it (three slots over 22 crew).
    public const int MaxSearch = 10_000;

    public bool Has(string id)
    {
        return byId.ContainsKey(id);
    }

    // Every trait its expressions read, in reading order, each once: for "where it came from".
    public IReadOnlyList<TraitRead> Reads(string id)
    {
        return byId[id].Reads;
    }

    // Whether a waiting storylet's option may be picked now (Task 6): it has no requires, or its requires holds on the cast.
    public bool Open(string storyletId, StoryletOption option, IStateSource state, IReadOnlyDictionary<string, string> cast)
    {
        return !optionRequires.TryGetValue((storyletId, option.Id), out CheckedExpression? requires) || book.Evaluate(requires, state, cast);
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
        bool firstHeld = false;
        Dictionary<string, string>? held = null;
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

        // A full binding: requires holds and it is not cooling. One that holds and is cooling is kept, the first, to say so.
        bool Complete()
        {
            if (compiled.Requires is not null && !Read(compiled.Requires))
            {
                return false;
            }

            if (!cooling(bound))
            {
                return true;
            }

            held ??= new Dictionary<string, string>(bound, StringComparer.Ordinal);
            return false;
        }

        CastResult Missed()
        {
            CastOutcome outcome = outOfTries ? CastOutcome.OutOfTries : held is not null ? CastOutcome.Cooling : CastOutcome.NoCast;
            return new CastResult(outcome, null, tries) { Held = outcome == CastOutcome.Cooling ? held : null, FirstHeld = firstHeld };
        }

        if (compiled.Slots.Count == 0)
        {
            bool holds = Complete();
            firstHeld = holds || held is not null;
            return holds ? new CastResult(CastOutcome.Cast, [], tries) { FirstHeld = true } : Missed();
        }

        List<string> order = Shuffled(roster, stream());

        bool Fill(int k)
        {
            if (k == compiled.Slots.Count)
            {
                return Complete();
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
                bool holds = Read(condition);
                firstHeld |= holds && k == 0;
                if (holds && Fill(k + 1))
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
            return new CastResult(CastOutcome.Cast, compiled.Slots.ToDictionary(s => s.Slot, s => bound[s.Slot], StringComparer.Ordinal), tries) { FirstHeld = true };
        }

        return Missed();
    }

    // Dev Play's search (design 4.10): the first top-level clause that held for no one. Each slot in turn is read over every crew
    // member in roster order not already bound, beside each cast the slots before it held for; its clauses narrow those casts one at
    // a time, and the first to leave none is the one. Then requires, over every full cast. No stream is drawn and no try counted:
    // it says why nothing held, which does not turn on the order. Null when every clause held for someone.
    public FailedClause? Failed(string id, IStateSource state, IReadOnlyList<string> roster)
    {
        Compiled compiled = byId[id];
        List<Dictionary<string, string>> casts = [new(StringComparer.Ordinal)];

        FailedClause? Narrow(string slot, CheckedExpression expression)
        {
            IReadOnlyList<CheckedExpression> clauses = book.Split(expression);
            for (int i = 0; i < clauses.Count; i++)
            {
                int before = casts.Count;
                casts = casts.Where(c => book.Evaluate(clauses[i], state, c)).ToList();
                if (casts.Count == 0)
                {
                    return new FailedClause(slot, clauses[i].Source, i == 0 ? null : before);
                }
            }

            return null;
        }

        foreach ((string slot, CheckedExpression condition) in compiled.Slots)
        {
            casts = casts.SelectMany(c => roster.Where(crewId => !c.ContainsValue(crewId)).Select(crewId => new Dictionary<string, string>(c, StringComparer.Ordinal) { [slot] = crewId })).ToList();
            if (casts.Count > MaxSearch)
            {
                return new FailedClause(slot, null, null);
            }

            if (Narrow(slot, condition) is { } failed)
            {
                return failed;
            }
        }

        return compiled.Requires is null ? null : Narrow(FailedClause.Requires, compiled.Requires);
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
    private CheckedExpression Checked(string id, string source, IReadOnlyList<string> readable, string key)
    {
        return book.Check(source, readable, out ExpressionError? error)
            ?? throw new InvalidOperationException($"{id}: {key}, at character {error!.Position + 1}: {error.Message}");
    }

    private Compiled Compile(string id, IReadOnlyList<CastSlot> cast, Expr? requires)
    {
        List<string> slots = cast.Select(c => c.Name).ToList();
        CheckedExpression Check(string source, IReadOnlyList<string> readable, string key)
        {
            return Checked(id, source, readable, key);
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

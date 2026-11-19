using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;

namespace Sim.Storylets;

// Dev Play's "Why these storylets" (1e part four, Task 7, design 4.10): Salience.Contend, the function the module runs when an
// itinerary opens, run again on a copy of the module's state with new copies of the streams. Review focus 3: it advances no
// stream and changes nothing. Casting and ties draw only from cast:<id>:<cycle> and salience:<cycle>, which nothing draws from
// before the itinerary opens, so a stream made new from the seed is the stream as the opening found it, whether or not the opening
// has drawn from it since; the legacy's saved streams are never read or written.
//
// In the itinerary it is the contest as it ran: the copy is put back as it stood before the opening's arrivals (this cycle's offers
// and fires taken out, and the storylets answered since put back as waiting), and what has changed since the opening (an answer's
// traits, morale, and loyalty, a vignette's effects, or the developer's) is read as it was then, from this cycle's events. Between
// cycles it is the next itinerary as things stand now: the copy is opened as the module opens it (the last cycle's rest marked, old
// history and stale offers dropped), but the week's recovery and a trait's change with the weeks, which come as the cycle begins,
// are not in it.
internal sealed class DryRun(ContentPack pack, Casting casting, LegacyState legacy, StoryletsState state, IRosterView roster, ITraitView traits) : IStoryletDryRun
{
    public Contest Contest(IReadOnlyList<IEvent> cycleEvents)
    {
        (StoryletsState copy, IStateSource source, int cycle, bool open) = Before(cycleEvents);
        Contest contest = Salience.Contend(pack, casting, copy, source, roster.Roster, cycle, name => RngStream.For(legacy.Seed, cycle, name));
        return contest with { Cycle = cycle, Open = open };
    }

    public FailedClause? Failed(string id, IReadOnlyList<IEvent> cycleEvents)
    {
        return casting.Has(id) ? casting.Failed(id, Before(cycleEvents).Source, roster.Roster) : null;
    }

    private (StoryletsState Copy, IStateSource Source, int Cycle, bool Open) Before(IReadOnlyList<IEvent> cycleEvents)
    {
        StoryletsState copy = Copy(state);
        if (legacy.Phase == CyclePhase.Itinerary)
        {
            int cycle = legacy.Cycle;
            List<IEvent> since = Since(cycleEvents, cycle);
            Rewind(copy, cycle, since);
            return (copy, new Rewound(new StateSource(roster, traits, copy, cycle), since), cycle, true);
        }

        int next = legacy.Cycle + 1;
        History.ItineraryOpened(copy, next, roster, pack.HistoryCycles);
        copy.Waiting.RemoveAll(w => pack.Storylets.All(s => s.Id != w.StoryletId) || w.Cast.Values.Any(id => !roster.Roster.Contains(id)));
        foreach (Fired fired in copy.Fired)
        {
            fired.Cycles.RemoveAll(c => next - c > pack.Salience.Window);
        }

        return (copy, new StateSource(roster, traits, copy, next), next, false);
    }

    // The events after this cycle's itinerary opened: the storylets module opens it on PhaseEntered, after every module that changes
    // a trait or a stat as the cycle begins, so all that follows came after the contest.
    private static List<IEvent> Since(IReadOnlyList<IEvent> cycleEvents, int cycle)
    {
        for (int i = cycleEvents.Count - 1; i >= 0; i--)
        {
            if (cycleEvents[i] is PhaseEntered { Phase: CyclePhase.Itinerary } entered && entered.Cycle == cycle)
            {
                return cycleEvents.Skip(i + 1).ToList();
            }
        }

        return [];
    }

    // The module's records as they stood when the contest ran: before this cycle's offers, arrivals, and fires, with any storylet
    // waiting from an earlier cycle and answered since put back. A cast cooldown's arrival that this cycle's replaced was older than
    // its cooldown (or it could not have arrived again), and so was the global cooldown's last fire, so neither is missed.
    private static void Rewind(StoryletsState copy, int cycle, List<IEvent> since)
    {
        copy.Waiting.RemoveAll(w => w.OfferId == StoryletsModule.OfferId(cycle, w.StoryletId));
        foreach (StoryletAnswered answered in since.OfType<StoryletAnswered>())
        {
            if (answered.OfferId != StoryletsModule.OfferId(cycle, answered.StoryletId) && copy.Waiting.All(w => w.OfferId != answered.OfferId))
            {
                copy.Waiting.Add(new WaitingStorylet { OfferId = answered.OfferId, StoryletId = answered.StoryletId, Cast = new(answered.Cast, StringComparer.Ordinal), EncounterId = null });
            }
        }

        copy.Arrivals.RemoveAll(a => a.Cycle == cycle);
        foreach (Fired fired in copy.Fired.ToList())
        {
            fired.Cycles.RemoveAll(c => c == cycle);
            if (fired.Last != cycle)
            {
                continue;
            }

            if (fired.Cycles.Count > 0)
            {
                fired.Last = fired.Cycles.Max();
            }
            else
            {
                copy.Fired.Remove(fired);
            }
        }
    }

    // Every list copied, so nothing the dry run does to its copy reaches the legacy. A field added to the state is added here.
    private static StoryletsState Copy(StoryletsState state)
    {
        return new StoryletsState
        {
            Waiting = state.Waiting.Select(w => new WaitingStorylet { OfferId = w.OfferId, StoryletId = w.StoryletId, Cast = new(w.Cast, StringComparer.Ordinal), EncounterId = w.EncounterId }).ToList(),
            History = state.History.Select(h => new HistoryCycle { Cycle = h.Cycle, Crew = h.Crew.ToDictionary(c => c.Key, c => c.Value.ToList()) }).ToList(),
            Vignettes = state.Vignettes.Select(v => new WaitingVignette { OfferId = v.OfferId, VignetteId = v.VignetteId, Cast = new(v.Cast, StringComparer.Ordinal) }).ToList(),
            Arrivals = state.Arrivals.Select(a => new Arrival { Id = a.Id, Cast = new(a.Cast, StringComparer.Ordinal), Cycle = a.Cycle }).ToList(),
            Fired = state.Fired.Select(f => new Fired { Id = f.Id, Last = f.Last, Cycles = f.Cycles.ToList() }).ToList(),
        };
    }

    // The state as the itinerary opened: each trait, morale, and loyalty changed since reads as the first change found it.
    private sealed class Rewound : IStateSource
    {
        private readonly IStateSource now;
        private readonly Dictionary<(string Trait, string? Crew, string? Target), int> traits = [];
        private readonly Dictionary<string, int> morale = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> loyalty = new(StringComparer.Ordinal);

        public Rewound(IStateSource now, IEnumerable<IEvent> since)
        {
            this.now = now;
            foreach (IEvent e in since)
            {
                switch (e)
                {
                    case TraitChanged changed:
                        traits.TryAdd((changed.Trait, changed.CrewId, changed.TargetId), changed.From);
                        break;
                    case MoraleChanged changed:
                        morale.TryAdd(changed.CrewId, changed.From);
                        break;
                    case LoyaltyChanged changed:
                        loyalty.TryAdd(changed.CrewId, changed.From);
                        break;
                }
            }
        }

        public int Cycle => now.Cycle;

        public int Morale(string crewId)
        {
            return morale.TryGetValue(crewId, out int then) ? then : now.Morale(crewId);
        }

        public int Loyalty(string crewId)
        {
            return loyalty.TryGetValue(crewId, out int then) ? then : now.Loyalty(crewId);
        }

        public int Skill(string crewId, string skill)
        {
            return now.Skill(crewId, skill);
        }

        public bool IsFit(string crewId)
        {
            return now.IsFit(crewId);
        }

        public bool IsAboard(string crewId)
        {
            return now.IsAboard(crewId);
        }

        public int Trait(string trait, string? crewId, string? targetId = null)
        {
            return traits.TryGetValue((trait, crewId, targetId), out int then) ? then : now.Trait(trait, crewId, targetId);
        }

        public int History(string crewId, string kind, int cycles)
        {
            return now.History(crewId, kind, cycles);
        }
    }
}

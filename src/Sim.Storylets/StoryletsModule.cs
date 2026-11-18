using Sim.Core;
using Sim.Core.Content;

namespace Sim.Storylets;

// Offers storylets and sends vignettes when the captain's itinerary opens, and applies answers' effects. It reaches crew only
// through the roster view and the AdjustLoyalty and AdjustMorale commands, traits only through the trait view and the
// ChangeTrait command (slice 1e, part three), and hears of missions only through their results.
public static class StoryletsModule
{
    public const string Name = "storylets";

    // After crew (100), missions (200), and traits (250): a death on the mission is already known, and the itinerary reads
    // traits as the mission left them.
    private const int Order = 300;

    private const string ShipKey = "ship";

    public static void Register(EventBus bus, ContentPack pack)
    {
        bus.Module<StoryletsState>(Name);
        bus.Guard<AnswerStorylet>(Order, Name, (command, context) => CheckAnswer(pack, command, context));
        bus.Guard<AdvancePhase>(Order, Name, (_, context) => context.State.Phase == CyclePhase.Itinerary && Answerable(pack, context).Any() ? AnswerStorylet.WaitingReason : null);
        bus.Handle<AnswerStorylet>((command, context) => OnAnswer(pack, command, context));
        bus.Guard<AnswerVignette>(Order, Name, (command, context) => CheckVignette(pack, command, context));
        bus.Handle<AnswerVignette>((command, context) => OnVignette(pack, command, context));
        bus.Subscribe<MissionEnded>(Order, Name, (e, context) => OnMissionEnded(e, context));
        bus.Subscribe<CrewDied>(Order, Name, (e, context) => OnCrewDied(e, context));
        bus.Subscribe<PhaseEntered>(Order, Name, (e, context) =>
        {
            StoryletsState state = context.Module<StoryletsState>();
            switch (e.Phase)
            {
                case CyclePhase.Aftermath when state.LastCycle?.Cycle != e.Cycle:
                    // No team was sent: the cycle still ran, and everyone fit rested.
                    state.LastCycle = new LastCycleRecord { Cycle = e.Cycle, Sent = [], Hurt = [], Died = [] };
                    break;
                case CyclePhase.Itinerary:
                    OnItinerary(pack, e.Cycle, context);
                    SendVignettes(pack, e.Cycle, context);
                    break;
                case CyclePhase.Orders:
                    CloseItinerary(pack, context);
                    state.LastCycle = null;
                    break;
            }
        });
    }

    // The waiting storylets the pack still has. One it has lost (the author deleted it while it waited) never holds the
    // legacy in the itinerary; it is withdrawn when the next itinerary opens.
    private static IEnumerable<WaitingStorylet> Answerable(ContentPack pack, IBusContext context)
    {
        return context.Module<StoryletsState>().Waiting.Where(w => pack.Storylets.Any(s => s.Id == w.StoryletId));
    }

    private static string? CheckAnswer(ContentPack pack, AnswerStorylet command, IBusContext context)
    {
        if (Answerable(pack, context).FirstOrDefault(w => w.OfferId == command.OfferId) is not { } waiting)
        {
            return "That storylet is not waiting for an answer.";
        }

        StoryletDef storylet = pack.Storylets.First(s => s.Id == waiting.StoryletId);
        return storylet.Options.Any(o => o.Id == command.OptionId) ? null : "That is not one of its answers.";
    }

    private static void OnAnswer(ContentPack pack, AnswerStorylet command, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        WaitingStorylet waiting = state.Waiting.First(w => w.OfferId == command.OfferId);
        StoryletOption option = pack.Storylets.First(s => s.Id == waiting.StoryletId).Options.First(o => o.Id == command.OptionId);
        Apply(option.Effects, waiting.SubjectId, TraitCauses.Answer, context);
        state.Waiting.Remove(waiting);
        context.Publish(new StoryletAnswered(waiting.OfferId, waiting.StoryletId, waiting.SubjectId, option.Id));
    }

    // An answer's or a vignette's effects, in order: loyalty and morale through the crew module's commands, on the subject or
    // every living crew member; traits through the traits module, on the ship or the subject (no on: the scope it arrived for).
    private static void Apply(IReadOnlyList<StoryletEffect> effects, string? subjectId, string cause, IBusContext context)
    {
        foreach (StoryletEffect effect in effects)
        {
            IEnumerable<string> Who(string? on)
            {
                return on == StoryletScopes.Crew ? context.View<IRosterView>().Roster : [subjectId!];
            }

            if (effect.Loyalty != 0)
            {
                foreach (string crewId in Who(effect.LoyaltyOn))
                {
                    context.Send(new AdjustLoyalty(crewId, effect.Loyalty));
                }
            }

            if (effect.Morale != 0)
            {
                foreach (string crewId in Who(effect.MoraleOn))
                {
                    context.Send(new AdjustMorale(crewId, effect.Morale));
                }
            }

            if (effect.Trait is { } change)
            {
                string? crewId = change.On == StoryletScopes.Ship ? null : subjectId;
                context.Send(new ChangeTrait(change.Trait, crewId, null, change.Amount, change.Set, cause));
            }
        }
    }

    private static string? CheckVignette(ContentPack pack, AnswerVignette command, IBusContext context)
    {
        if (context.Module<StoryletsState>().Vignettes.FirstOrDefault(v => v.OfferId == command.OfferId) is not { } waiting
            || pack.Vignettes.FirstOrDefault(v => v.Id == waiting.VignetteId) is not { } vignette)
        {
            return "That vignette is not waiting for an answer.";
        }

        return vignette.Options.Any(o => o.Id == command.OptionId) ? null : "That is not one of its answers.";
    }

    private static void OnVignette(ContentPack pack, AnswerVignette command, IBusContext context)
    {
        WaitingVignette waiting = context.Module<StoryletsState>().Vignettes.First(v => v.OfferId == command.OfferId);
        Settle(pack.Vignettes.First(v => v.Id == waiting.VignetteId), waiting, command.OptionId, defaulted: false, context);
    }

    private static void Settle(VignetteDef vignette, WaitingVignette waiting, string optionId, bool defaulted, IBusContext context)
    {
        Apply(vignette.Options.First(o => o.Id == optionId).Effects, waiting.SubjectId, TraitCauses.Vignette, context);
        context.Module<StoryletsState>().Vignettes.Remove(waiting);
        context.Publish(new VignetteAnswered(waiting.OfferId, waiting.VignetteId, waiting.SubjectId, optionId, defaulted));
    }

    // The itinerary closes: each vignette still waiting takes its default. One the pack has lost is dropped with no effect.
    private static void CloseItinerary(ContentPack pack, IBusContext context)
    {
        foreach (WaitingVignette waiting in context.Module<StoryletsState>().Vignettes.ToList())
        {
            if (pack.Vignettes.FirstOrDefault(v => v.Id == waiting.VignetteId) is { Default: { } choice } vignette && vignette.Options.Any(o => o.Id == choice))
            {
                Settle(vignette, waiting, choice, defaulted: true, context);
            }
            else
            {
                context.Module<StoryletsState>().Vignettes.Remove(waiting);
            }
        }
    }

    // After the storylets: every vignette whose requirements hold on a scope and which is out of its cooldown there. Those that
    // have waited longest since they last arrived anywhere come first (never first of all), then by id, then by the subject's
    // place on the roster. The rest are dropped: a vignette is not a debt.
    private static void SendVignettes(ContentPack pack, int cycle, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        IReadOnlyList<string> roster = context.View<IRosterView>().Roster;
        int Last(string vignetteId, string? subjectId, bool anyScope)
        {
            return state.Arrivals.Where(a => a.VignetteId == vignetteId && (anyScope || a.SubjectId == subjectId)).Select(a => a.Cycle).DefaultIfEmpty(int.MinValue).Max();
        }

        var due = new List<(VignetteDef Vignette, string? SubjectId, int Position)>();
        foreach (VignetteDef vignette in pack.Vignettes)
        {
            IEnumerable<(string? Id, int Position)> scopes = vignette.Scope == StoryletScopes.Crew
                ? roster.Select((id, position) => ((string?)id, position))
                : [(null, -1)];
            foreach ((string? subjectId, int position) in scopes)
            {
                int last = Last(vignette.Id, subjectId, anyScope: false);
                bool cooling = last != int.MinValue && cycle - last < pack.VignetteCooldown;
                if (!cooling && vignette.Requires.All(r => Holds(r, state, subjectId, context)))
                {
                    due.Add((vignette, subjectId, position));
                }
            }
        }

        IEnumerable<(VignetteDef Vignette, string? SubjectId, int Position)> ordered = due
            .OrderBy(d => Last(d.Vignette.Id, null, anyScope: true))
            .ThenBy(d => d.Vignette.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Position);
        foreach ((VignetteDef vignette, string? subjectId, _) in ordered.Take(pack.VignettesPerCycle).ToList())
        {
            var offer = new WaitingVignette { OfferId = $"{cycle}:{vignette.Id}:{subjectId ?? ShipKey}", VignetteId = vignette.Id, SubjectId = subjectId, Fact = vignette.Fact };
            state.Arrivals.RemoveAll(a => a.VignetteId == vignette.Id && a.SubjectId == subjectId);
            state.Arrivals.Add(new VignetteArrival { VignetteId = vignette.Id, SubjectId = subjectId, Cycle = cycle });
            context.Publish(new VignetteArrived(offer.OfferId, vignette.Id, subjectId, offer.Fact));
            if (vignette.Options.Count == 0)
            {
                Apply(vignette.Effects, subjectId, TraitCauses.Vignette, context);
                context.Publish(new VignetteAnswered(offer.OfferId, vignette.Id, subjectId, null, Defaulted: false));
            }
            else
            {
                state.Vignettes.Add(offer);
            }
        }
    }

    private static void OnMissionEnded(MissionEnded ended, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        IReadOnlyList<CrewResult> crew = ended.Result.Crew;
        state.LastCycle = new LastCycleRecord
        {
            Cycle = context.State.Cycle,
            Sent = crew.Where(c => c.Status != MemberStatus.Dead).Select(c => c.Id).ToList(),
            Hurt = crew.Where(c => c.Status == MemberStatus.Incapacitated || (c.Status == MemberStatus.Active && c.Injury > 0)).Select(c => c.Id).ToList(),
            Died = crew.Where(c => c.Status == MemberStatus.Dead).Select(c => c.Id).ToList(),
        };
    }

    private static void OnCrewDied(CrewDied died, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        state.Vignettes.RemoveAll(v => v.SubjectId == died.CrewId);
        foreach (WaitingStorylet waiting in state.Waiting.Where(w => w.SubjectId == died.CrewId).ToList())
        {
            Withdraw(state, waiting, context);
        }
    }

    private static void OnItinerary(ContentPack pack, int cycle, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        IReadOnlyList<string> roster = context.View<IRosterView>().Roster;
        foreach (WaitingStorylet stale in state.Waiting.Where(w => pack.Storylets.All(s => s.Id != w.StoryletId) || (w.SubjectId is not null && !roster.Contains(w.SubjectId))).ToList())
        {
            Withdraw(state, stale, context);
        }

        // Every storylet whose requirements hold on a scope it is not already waiting for: highest stakes first, then by id, then
        // by the subject's place on the roster. The rest wait for a later cycle.
        var due = new List<(StoryletDef Storylet, string? SubjectId, int Position)>();
        foreach (StoryletDef storylet in pack.Storylets)
        {
            IEnumerable<(string? Id, int Position)> scopes = storylet.Scope == StoryletScopes.Crew
                ? roster.Select((id, position) => ((string?)id, position))
                : [(null, -1)];
            foreach ((string? subjectId, int position) in scopes)
            {
                bool waiting = state.Waiting.Any(w => w.StoryletId == storylet.Id && w.SubjectId == subjectId);
                if (!waiting && storylet.Requires.All(r => Holds(r, state, subjectId, context)))
                {
                    due.Add((storylet, subjectId, position));
                }
            }
        }

        foreach ((StoryletDef storylet, string? subjectId, _) in due.OrderByDescending(d => d.Storylet.Stakes).ThenBy(d => d.Storylet.Id, StringComparer.Ordinal).ThenBy(d => d.Position))
        {
            if (state.Waiting.Count >= pack.StoryletsPerCycle)
            {
                break;
            }

            // Where it came from: the encounter that last changed the first required trait that is set.
            ITraitView traits = context.View<ITraitView>();
            string? encounterId = storylet.Traits
                .Where(r => traits.Value(r.Trait, subjectId) > 0)
                .Select(r => traits.EncounterId(r.Trait, subjectId))
                .FirstOrDefault();
            var offer = new WaitingStorylet
            {
                OfferId = $"{cycle}:{storylet.Id}:{subjectId ?? ShipKey}",
                StoryletId = storylet.Id,
                SubjectId = subjectId,
                EncounterId = encounterId,
            };
            state.Waiting.Add(offer);
            context.Publish(new StoryletOffered(offer.OfferId, offer.StoryletId, offer.SubjectId, offer.EncounterId));
        }
    }

    private static void Withdraw(StoryletsState state, WaitingStorylet waiting, IBusContext context)
    {
        state.Waiting.Remove(waiting);
        context.Publish(new StoryletWithdrawn(waiting.OfferId, waiting.StoryletId, waiting.SubjectId));
    }

    // One requirement on one scope: the ship's traits (subjectId null), or a crew member's traits, stats, and last cycle.
    private static bool Holds(Requirement requirement, StoryletsState state, string? subjectId, IBusContext context)
    {
        IRosterView roster = context.View<IRosterView>();
        LastCycleRecord? last = state.LastCycle;
        return requirement switch
        {
            TraitRange trait => trait.Holds(context.View<ITraitView>().Value(trait.Trait, subjectId)),
            StatRange stat when subjectId is not null => stat.Holds(stat.Stat == StoryletStats.Morale ? roster.Morale(subjectId) : roster.Loyalty(subjectId)),
            LastCycle fact when subjectId is not null && last is not null => fact.Not != fact.Fact switch
            {
                LastCycleFacts.Sent => last.Sent.Contains(subjectId),
                LastCycleFacts.Hurt => last.Hurt.Contains(subjectId),
                LastCycleFacts.Rested => !last.Sent.Contains(subjectId) && !last.Died.Contains(subjectId) && roster.IsFit(subjectId),
                LastCycleFacts.LostCrewmate => last.Died.Count > 0 && !last.Died.Contains(subjectId),
                _ => false,
            },
            _ => false,
        };
    }
}

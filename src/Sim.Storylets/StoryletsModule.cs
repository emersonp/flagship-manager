using Sim.Core;
using Sim.Core.Content;

namespace Sim.Storylets;

// Offers storylets and sends vignettes when the captain's itinerary opens, and applies answers' effects. It reaches crew only
// through the roster view and the AdjustLoyalty and AdjustMorale commands, traits only through the trait view and the
// ChangeTrait command (slice 1e, part three), and hears of missions only through their results. From part four each storylet and
// vignette is cast with named slots, read in the expression language (design 4.4), and arrives at most once a cycle, ranked by
// salience and held back by its cooldowns (design 4.5 and 4.6; Salience.cs).
public static class StoryletsModule
{
    public const string Name = "storylets";

    // After crew (100), missions (200), and traits (250): a death on the mission is already known, and the itinerary reads
    // traits as the mission left them.
    private const int Order = 300;

    public static void Register(EventBus bus, ContentPack pack)
    {
        var casting = new Casting(pack);
        bus.Module<StoryletsState>(Name);
        bus.Provide<IHistoryView>(state => new HistoryView(state.Module<StoryletsState>(Name), state.Cycle));
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
                case CyclePhase.Aftermath:
                    // When no team was sent, the cycle still ran.
                    History.AftermathBegan(state, e.Cycle);
                    break;
                case CyclePhase.Itinerary:
                    History.ItineraryOpened(state, e.Cycle, context.View<IRosterView>(), pack.HistoryCycles);
                    OnItinerary(pack, casting, e.Cycle, context);
                    break;
                case CyclePhase.Orders:
                    CloseItinerary(pack, context);
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
        StoryletDef storylet = pack.Storylets.First(s => s.Id == waiting.StoryletId);
        StoryletOption option = storylet.Options.First(o => o.Id == command.OptionId);
        Apply(option.Effects, storylet.Cast, waiting.Cast, TraitCauses.Answer, context);
        state.Waiting.Remove(waiting);
        context.Publish(new StoryletAnswered(waiting.OfferId, waiting.StoryletId, Copy(waiting.Cast), option.Id));
    }

    // An answer's or a vignette's effects, in order: loyalty and morale through the crew module's commands, on a slot or every
    // living crew member; traits through the traits module, on the ship or a slot (no on: the first slot, or the ship when there
    // is no cast), and toward a slot for a pair trait.
    private static void Apply(IReadOnlyList<StoryletEffect> effects, IReadOnlyList<CastSlot> slots, Dictionary<string, string> cast, string cause, IBusContext context)
    {
        string? first = slots.Count == 0 ? null : cast[slots[0].Name];
        foreach (StoryletEffect effect in effects)
        {
            IEnumerable<string> Who(string? on)
            {
                return on == StoryletScopes.Crew ? context.View<IRosterView>().Roster : [cast[on!]];
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
                string? crewId = change.On == StoryletScopes.Ship ? null : change.On.Length == 0 ? first : cast[change.On];
                context.Send(new ChangeTrait(change.Trait, crewId, change.Toward is null ? null : cast[change.Toward], change.Amount, change.Set, cause));
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
        Apply(vignette.Options.First(o => o.Id == optionId).Effects, vignette.Cast, waiting.Cast, TraitCauses.Vignette, context);
        context.Module<StoryletsState>().Vignettes.Remove(waiting);
        context.Publish(new VignetteAnswered(waiting.OfferId, waiting.VignetteId, Copy(waiting.Cast), optionId, defaulted));
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

    private static void OnMissionEnded(MissionEnded ended, IBusContext context)
    {
        History.MissionEnded(context.Module<StoryletsState>(), context.State.Cycle, ended.Result, context.View<IRosterView>().Roster);
    }

    // Review focus 5: a dead cast member withdraws the offer, whichever slot they held, and takes their vignettes and arrivals with
    // them.
    private static void OnCrewDied(CrewDied died, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        History.Forget(state, died.CrewId);
        state.Vignettes.RemoveAll(v => v.Cast.ContainsValue(died.CrewId));
        state.Arrivals.RemoveAll(a => a.Cast.ContainsValue(died.CrewId));
        foreach (WaitingStorylet waiting in state.Waiting.Where(w => w.Cast.ContainsValue(died.CrewId)).ToList())
        {
            Withdraw(state, waiting, context);
        }
    }

    // The itinerary opens (design 4.4 to 4.6): the stale are withdrawn, the contest is run, and what it says arrived arrives, the
    // storylets and then the vignettes, each in salience order. A storylet that lost on the budget is cast again next cycle; a
    // vignette that lost is dropped, since a vignette is not a debt.
    private static void OnItinerary(ContentPack pack, Casting casting, int cycle, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        IReadOnlyList<string> roster = context.View<IRosterView>().Roster;
        foreach (WaitingStorylet stale in state.Waiting.Where(w => pack.Storylets.All(s => s.Id != w.StoryletId) || w.Cast.Values.Any(id => !roster.Contains(id))).ToList())
        {
            Withdraw(state, stale, context);
        }

        foreach (Fired fired in state.Fired)
        {
            fired.Cycles.RemoveAll(c => cycle - c > pack.Salience.Window);
        }

        ITraitView traits = context.View<ITraitView>();
        var source = new StateSource(context.View<IRosterView>(), traits, state, cycle);
        Contest contest = Salience.Contend(pack, casting, state, source, roster, cycle, context.Rng);
        foreach (Contender arrived in contest.Storylets.Where(c => c.Fate == Fate.Arrived))
        {
            Dictionary<string, string> cast = Copy(arrived.Cast!);

            // Where it came from: the encounter that last changed the first trait its expressions read that is set.
            string? encounterId = casting.Reads(arrived.Id)
                .Select(r => (r.Trait, Crew: r.Slot == StoryletScopes.Ship ? null : cast[r.Slot], Target: r.Toward is null ? null : cast[r.Toward]))
                .Where(r => traits.Value(r.Trait, r.Crew, r.Target) > 0)
                .Select(r => traits.EncounterId(r.Trait, r.Crew, r.Target))
                .FirstOrDefault();
            var offer = new WaitingStorylet { OfferId = OfferId(cycle, arrived.Id), StoryletId = arrived.Id, Cast = cast, EncounterId = encounterId };
            state.Waiting.Add(offer);
            Arrived(state, arrived.Id, cast, cycle);
            context.Publish(new StoryletOffered(offer.OfferId, offer.StoryletId, Copy(cast), offer.EncounterId, arrived.Score!.Total));
        }

        foreach (Contender arrived in contest.Vignettes.Where(c => c.Fate == Fate.Arrived))
        {
            VignetteDef vignette = pack.Vignettes.First(v => v.Id == arrived.Id);
            Dictionary<string, string> cast = Copy(arrived.Cast!);
            var offer = new WaitingVignette { OfferId = OfferId(cycle, vignette.Id), VignetteId = vignette.Id, Cast = cast };
            Arrived(state, vignette.Id, cast, cycle);
            context.Publish(new VignetteArrived(offer.OfferId, vignette.Id, Copy(cast), arrived.Score!.Total));
            if (vignette.Options.Count == 0)
            {
                Apply(vignette.Effects, vignette.Cast, cast, TraitCauses.Vignette, context);
                context.Publish(new VignetteAnswered(offer.OfferId, vignette.Id, Copy(cast), null, Defaulted: false));
            }
            else
            {
                state.Vignettes.Add(offer);
            }
        }
    }

    private static void Withdraw(StoryletsState state, WaitingStorylet waiting, IBusContext context)
    {
        state.Waiting.Remove(waiting);
        context.Publish(new StoryletWithdrawn(waiting.OfferId, waiting.StoryletId, Copy(waiting.Cast)));
    }

    // Its cast's latest arrival, for the cast cooldown, and its arrivals over every cast, for the global cooldown and freshness.
    private static void Arrived(StoryletsState state, string id, Dictionary<string, string> cast, int cycle)
    {
        state.Arrivals.RemoveAll(a => a.Id == id && Salience.Same(a.Cast, cast));
        state.Arrivals.Add(new Arrival { Id = id, Cast = Copy(cast), Cycle = cycle });
        if (state.Fired.FirstOrDefault(f => f.Id == id) is { } fired)
        {
            fired.Last = cycle;
            fired.Cycles.Add(cycle);
        }
        else
        {
            state.Fired.Add(new Fired { Id = id, Last = cycle, Cycles = [cycle] });
        }
    }

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> cast)
    {
        return new Dictionary<string, string>(cast, StringComparer.Ordinal);
    }

    // {cycle}:{id}: one cast a cycle.
    private static string OfferId(int cycle, string id)
    {
        return $"{cycle}:{id}";
    }
}

using Sim.Core;
using Sim.Core.Content;

namespace Sim.Storylets;

// Owns the story flags, offers storylets when the aftermath begins, and applies an answer's effects. It reaches crew only
// through the roster view and the AdjustLoyalty command, and hears of missions only through their results.
public static class StoryletsModule
{
    public const string Name = "storylets";

    // After crew (100) and missions (200): a death on the mission is already known when its flags are stored.
    private const int Order = 300;

    private const string ShipKey = "ship";

    public static void Register(EventBus bus, ContentPack pack)
    {
        bus.Module<StoryletsState>(Name);
        bus.Guard<AnswerStorylet>(Order, Name, (command, context) => CheckAnswer(pack, command, context));
        bus.Guard<AdvancePhase>(Order, Name, (_, context) => context.State.Phase == CyclePhase.Aftermath && Answerable(pack, context).Any() ? AnswerStorylet.WaitingReason : null);
        bus.Handle<AnswerStorylet>((command, context) => OnAnswer(pack, command, context));
        bus.Guard<SetFlag>(Order, Name, (command, context) => CheckSet(command, context));
        bus.Handle<SetFlag>((command, context) => OnSet(command, context));
        bus.Subscribe<MissionEnded>(Order, Name, (e, context) => OnMissionEnded(e, context));
        bus.Subscribe<CrewDied>(Order, Name, (e, context) => OnCrewDied(e, context));
        bus.Subscribe<PhaseEntered>(Order, Name, (e, context) =>
        {
            if (e.Phase == CyclePhase.Aftermath)
            {
                OnAftermath(pack, e.Cycle, context);
            }
        });
    }

    // The waiting storylets the pack still has. One it has lost (the author deleted it while it waited) never holds the
    // legacy in the aftermath; it is withdrawn when the next aftermath begins.
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
        foreach (StoryletEffect effect in option.Effects)
        {
            if (effect.Loyalty != 0)
            {
                IEnumerable<string> crew = effect.LoyaltyOn == StoryletScopes.Crew ? context.View<IRosterView>().Roster : [waiting.SubjectId!];
                foreach (string crewId in crew)
                {
                    context.Send(new AdjustLoyalty(crewId, effect.Loyalty));
                }
            }

            if (effect.Flag is { } change)
            {
                string? crewId = change.On == StoryletScopes.Ship ? null : waiting.SubjectId;
                Change(state, change.Flag, crewId, change.Amount, change.Set, null, context);
            }
        }

        state.Waiting.Remove(waiting);
        context.Publish(new StoryletAnswered(waiting.OfferId, waiting.StoryletId, waiting.SubjectId, option.Id));
    }

    private static string? CheckSet(SetFlag command, IBusContext context)
    {
        if (string.IsNullOrEmpty(command.Flag) || !Ids.IsInnerId(command.Flag))
        {
            return $"Flag id '{command.Flag}' must be {char.ToLowerInvariant(Ids.InnerRule[0])}{Ids.InnerRule[1..]}";
        }

        if (command.Value is < 0 or > SetFlag.Most)
        {
            return $"A flag's value is 0 to {SetFlag.Most}.";
        }

        if (command.CrewId is not null && !context.View<IRosterView>().Roster.Contains(command.CrewId))
        {
            return $"Crew member '{command.CrewId}' is not on the roster.";
        }

        return Value(context.Module<StoryletsState>(), command.Flag, command.CrewId) == command.Value
            ? $"{command.Flag} is already {command.Value} {(command.CrewId is null ? "on the ship" : $"on {command.CrewId}")}."
            : null;
    }

    // An answer's kind of change: it keeps the encounter that last changed the flag, so a storylet's head still says where it came from.
    private static void OnSet(SetFlag command, IBusContext context)
    {
        context.Publish(new FlagSetByDeveloper(command.Flag, command.CrewId, command.Value));
        Change(context.Module<StoryletsState>(), command.Flag, command.CrewId, command.Value, true, null, context);
    }

    private static void OnMissionEnded(MissionEnded ended, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        IReadOnlyList<string> roster = context.View<IRosterView>().Roster;
        foreach (MissionFlag flag in ended.Result.Flags)
        {
            // Someone who died on the mission has left the roster by now; what the mission set on them goes with them.
            if (flag.CrewId is not null && !roster.Contains(flag.CrewId))
            {
                continue;
            }

            Change(state, flag.Flag, flag.CrewId, flag.Amount, flag.Set, flag.EncounterId, context);
        }
    }

    private static void OnCrewDied(CrewDied died, IBusContext context)
    {
        StoryletsState state = context.Module<StoryletsState>();
        state.Flags.RemoveAll(f => f.CrewId == died.CrewId);
        foreach (WaitingStorylet waiting in state.Waiting.Where(w => w.SubjectId == died.CrewId).ToList())
        {
            Withdraw(state, waiting, context);
        }
    }

    private static void OnAftermath(ContentPack pack, int cycle, IBusContext context)
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
                if (!waiting && storylet.Requires.All(r => r.Holds(Value(state, r.Flag, subjectId))))
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

            // Where it came from: the encounter that last changed the first required flag that is set.
            string? encounterId = storylet.Requires
                .Select(r => Find(state, r.Flag, subjectId))
                .FirstOrDefault(f => f is not null)?.EncounterId;
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

    // Adds to or sets one flag. It never goes below 0, a flag at 0 is not stored, and a change that changes nothing is not published.
    // encounterId null (an answer) keeps the encounter that last changed the flag.
    private static void Change(StoryletsState state, string flag, string? crewId, int amount, bool set, string? encounterId, IBusContext context)
    {
        StoredFlag? stored = Find(state, flag, crewId);
        int from = stored?.Value ?? 0;
        int to = Math.Max(0, set ? amount : from + amount);
        if (to == from)
        {
            return;
        }

        if (to == 0)
        {
            state.Flags.Remove(stored!);
        }
        else if (stored is null)
        {
            state.Flags.Add(new StoredFlag { Flag = flag, CrewId = crewId, Value = to, SetAtWeek = context.State.Week, EncounterId = encounterId });
        }
        else
        {
            stored.Value = to;
            stored.SetAtWeek = context.State.Week;
            stored.EncounterId = encounterId ?? stored.EncounterId;
        }

        context.Publish(new FlagChanged(flag, crewId, from, to, encounterId));
    }

    private static StoredFlag? Find(StoryletsState state, string flag, string? crewId)
    {
        return state.Flags.FirstOrDefault(f => f.Flag == flag && f.CrewId == crewId);
    }

    private static int Value(StoryletsState state, string flag, string? crewId)
    {
        return Find(state, flag, crewId)?.Value ?? 0;
    }
}

using Sim.Core;
using Sim.Core.Content;

namespace Sim.Traits;

// Owns the traits (slice 1e, part three): on the ship, on a crew member, or on a pair. It applies a mission's trait changes
// when the mission ends, the changes other modules send (ChangeTrait), and Dev Play's (SetTrait), and lets go of the dead.
// Other modules read traits through ITraitView.
public static class TraitsModule
{
    public const string Name = "traits";

    // After crew (100) and missions (200), so a death on the mission is already known when its trait changes are applied; before
    // storylets (300), so the itinerary reads traits as they now stand.
    private const int Order = 250;

    public static void Register(EventBus bus, ContentPack pack)
    {
        bus.Module<TraitsState>(Name);
        // The factory reads the state on every call: the state may not exist until a trait is first set.
        bus.Provide<ITraitView>(state => new TraitView(state.Module<TraitsState>(Name)));
        bus.Handle<ChangeTrait>((command, context) => Change(context, command.Trait, command.CrewId, command.TargetId, command.Amount, command.Set, command.Cause, command.EncounterId));
        bus.Guard<SetTrait>(Order, Name, (command, context) => CheckSet(command, context));
        bus.Handle<SetTrait>((command, context) => OnSet(command, context));
        bus.Subscribe<MissionEnded>(Order, Name, (e, context) => OnMissionEnded(e, context));
        bus.Subscribe<CrewDied>(Order, Name, (e, context) => OnCrewDied(e, context));
    }

    internal static StoredTrait? Find(TraitsState state, string trait, string? crewId, string? targetId)
    {
        return state.Traits.FirstOrDefault(t => t.Trait == trait && t.CrewId == crewId && t.TargetId == targetId);
    }

    private static string? CheckSet(SetTrait command, IBusContext context)
    {
        if (string.IsNullOrEmpty(command.Trait) || !Ids.IsInnerId(command.Trait))
        {
            return $"Trait id '{command.Trait}' must be {char.ToLowerInvariant(Ids.InnerRule[0])}{Ids.InnerRule[1..]}";
        }

        if (command.Value is < 0 or > SetTrait.Most)
        {
            return $"A trait's value is 0 to {SetTrait.Most}.";
        }

        IReadOnlyList<string> roster = context.View<IRosterView>().Roster;
        if (command.CrewId is not null && !roster.Contains(command.CrewId))
        {
            return $"Crew member '{command.CrewId}' is not on the roster.";
        }

        if (command.TargetId is not null && !roster.Contains(command.TargetId))
        {
            return $"Crew member '{command.TargetId}' is not on the roster.";
        }

        int value = Find(context.Module<TraitsState>(), command.Trait, command.CrewId, command.TargetId)?.Value ?? 0;
        return value == command.Value
            ? $"{command.Trait} is already {command.Value} {(command.CrewId is null ? "on the ship" : $"on {command.CrewId}")}."
            : null;
    }

    // Keeps the encounter that last changed the trait, so a storylet's head still says where it came from.
    private static void OnSet(SetTrait command, IBusContext context)
    {
        context.Publish(new TraitSetByDeveloper(command.Trait, command.CrewId, command.TargetId, command.Value));
        Change(context, command.Trait, command.CrewId, command.TargetId, command.Value, true, TraitCauses.Developer, null);
    }

    private static void OnMissionEnded(MissionEnded ended, IBusContext context)
    {
        IReadOnlyList<string> roster = context.View<IRosterView>().Roster;
        foreach (MissionTrait trait in ended.Result.Traits)
        {
            // Someone who died on the mission has left the roster by now; what the mission set on them goes with them.
            if (trait.CrewId is not null && !roster.Contains(trait.CrewId))
            {
                continue;
            }

            Change(context, trait.Trait, trait.CrewId, null, trait.Amount, trait.Set, TraitCauses.Mission, trait.EncounterId);
        }
    }

    private static void OnCrewDied(CrewDied died, IBusContext context)
    {
        context.Module<TraitsState>().Traits.RemoveAll(t => t.CrewId == died.CrewId);
    }

    // Adds to or sets one trait. It never goes below 0, a trait at 0 is not stored, and a change that changes nothing is not
    // published. encounterId null keeps the encounter that last changed the trait.
    private static void Change(IBusContext context, string trait, string? crewId, string? targetId, int amount, bool set, string cause, string? encounterId)
    {
        TraitsState state = context.Module<TraitsState>();
        StoredTrait? stored = Find(state, trait, crewId, targetId);
        int from = stored?.Value ?? 0;
        int to = Math.Max(0, set ? amount : from + amount);
        if (to == from)
        {
            return;
        }

        if (to == 0)
        {
            state.Traits.Remove(stored!);
        }
        else if (stored is null)
        {
            state.Traits.Add(new StoredTrait { Trait = trait, CrewId = crewId, TargetId = targetId, Value = to, ChangedAtWeek = context.State.Week, EncounterId = encounterId });
        }
        else
        {
            stored.Value = to;
            stored.ChangedAtWeek = context.State.Week;
            stored.EncounterId = encounterId ?? stored.EncounterId;
        }

        context.Publish(new TraitChanged(trait, crewId, targetId, from, to, cause, encounterId));
    }
}

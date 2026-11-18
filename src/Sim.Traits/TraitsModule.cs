using Sim.Core;
using Sim.Core.Content;

namespace Sim.Traits;

// Owns the traits (slice 1e, part three): on the ship, on a crew member, or on a pair, and every rule their definitions give
// them. It applies a mission's trait changes when the mission ends, the changes other modules send (ChangeTrait), and Dev
// Play's (SetTrait); it steps and expires traits each week, and lets go of the dead. Other modules read traits through ITraitView.
// No rule here names a trait: every rule is read from the pack's definitions.
public static class TraitsModule
{
    public const string Name = "traits";

    // After crew (100) and missions (200), so a death on the mission is already known when its trait changes are applied; before
    // storylets (300), so the itinerary reads traits as they now stand.
    private const int Order = 250;

    public static void Register(EventBus bus, ContentPack pack)
    {
        var defs = new Definitions(pack.Traits);
        bus.Module<TraitsState>(Name);
        // The factory reads the state on every call: the state may not exist until a trait is first set.
        bus.Provide<ITraitView>(state => new TraitView(state.Module<TraitsState>(Name), defs));
        // Content the loader accepted never adds to a trait with levels; this keeps anything else from doing so.
        bus.Guard<ChangeTrait>(Order, Name, (command, _) => !command.Set && command.Amount != 0 && defs[command.Trait].HasLevels ? defs[command.Trait].LevelsAreSet : null);
        bus.Handle<ChangeTrait>((command, context) => Change(defs, context, command.Trait, command.CrewId, command.TargetId, command.Amount, command.Set, command.Cause, command.EncounterId));
        bus.Guard<SetTrait>(Order, Name, (command, context) => CheckSet(defs, command, context));
        bus.Handle<SetTrait>((command, context) => OnSet(defs, command, context));
        bus.Subscribe<MissionStarted>(Order, Name, (e, context) => OnMissionStarted(defs, e, context));
        bus.Subscribe<MissionEnded>(Order, Name, (e, context) => OnMissionEnded(defs, e, context));
        bus.Subscribe<WeekAdvanced>(Order, Name, (e, context) => OnWeek(defs, e, context));
        bus.Subscribe<CrewDied>(Order, Name, (e, context) => OnCrewDied(defs, e, context));
    }

    internal static StoredTrait? Find(TraitsState state, string trait, string? crewId, string? targetId)
    {
        return state.Traits.FirstOrDefault(t => t.Trait == trait && t.CrewId == crewId && t.TargetId == targetId);
    }

    private static string? CheckSet(Definitions defs, SetTrait command, IBusContext context)
    {
        if (string.IsNullOrEmpty(command.Trait) || !Ids.IsInnerId(command.Trait))
        {
            return $"Trait id '{command.Trait}' must be {char.ToLowerInvariant(Ids.InnerRule[0])}{Ids.InnerRule[1..]}";
        }

        TraitDef def = defs[command.Trait];
        if (command.Value is < 0 or > SetTrait.Most)
        {
            return $"A trait's value is 0 to {SetTrait.Most}.";
        }

        if (def.HasLevels && command.Value != 0 && def.LevelName(command.Value) is null)
        {
            return $"{command.Trait} is set to 0 or one of its levels: {string.Join(", ", def.Levels.OrderBy(l => l.Key).Select(l => $"{l.Value} ({l.Key})"))}.";
        }

        if (command.Value > def.Cap)
        {
            return $"{command.Trait} is at most {def.Cap}.";
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
    private static void OnSet(Definitions defs, SetTrait command, IBusContext context)
    {
        context.Publish(new TraitSetByDeveloper(command.Trait, command.CrewId, command.TargetId, command.Value));
        Change(defs, context, command.Trait, command.CrewId, command.TargetId, command.Value, true, TraitCauses.Developer, null);
    }

    // The launch rules (design 4.6): exactly the entries of the preview whose condition this team meets, in the preview's order, so
    // what the briefing showed is what happens. The team's check bonuses were fixed before this, as the crew walked on board.
    private static void OnMissionStarted(Definitions defs, MissionStarted started, IBusContext context)
    {
        IReadOnlyList<LaunchPreview> preview = Preview(context.Module<TraitsState>(), defs, context.View<IRosterView>().Roster);
        foreach (LaunchPreview rule in preview.Where(p => p.Fires(started.CrewIds)))
        {
            Apply(defs, context, defs[rule.Trait], rule.HolderId, rule.TargetId, rule.Effects, TraitCauses.Launch);
        }
    }

    // The preview for a page that reads the stored state (the briefing): the same function the launch runs.
    public static IReadOnlyList<LaunchPreview> Preview(TraitsState state, IReadOnlyList<TraitDef> defs, IReadOnlyList<string> roster)
    {
        return Preview(state, new Definitions(defs), roster);
    }

    // Every launch rule that could fire: holders in roster order, each holder's traits in definition order (a trait with no
    // definition has none), a trait's pairs in their targets' roster order; for each, sent, then notSent, then withTarget.
    internal static IReadOnlyList<LaunchPreview> Preview(TraitsState state, Definitions defs, IReadOnlyList<string> roster)
    {
        var preview = new List<LaunchPreview>();
        foreach (string holder in roster)
        {
            foreach (TraitDef def in defs.All.Where(d => d.Launch is not null))
            {
                TraitLaunch launch = def.Launch!;
                IEnumerable<StoredTrait> held = state.Traits
                    .Where(t => t.CrewId == holder && t.Trait == def.Id && (t.TargetId is null || roster.Contains(t.TargetId)))
                    .OrderBy(t => t.TargetId is null ? -1 : roster.ToList().IndexOf(t.TargetId));
                foreach (StoredTrait trait in held)
                {
                    if (launch.Sent.Count > 0)
                    {
                        preview.Add(new LaunchPreview(def.Id, holder, trait.TargetId, LaunchWhen.Sent, launch.Sent));
                    }

                    if (launch.NotSent.Count > 0)
                    {
                        preview.Add(new LaunchPreview(def.Id, holder, trait.TargetId, LaunchWhen.NotSent, launch.NotSent));
                    }

                    if (trait.TargetId is not null && launch.WithTarget.Count > 0)
                    {
                        preview.Add(new LaunchPreview(def.Id, holder, trait.TargetId, LaunchWhen.WithTarget, launch.WithTarget));
                    }
                }
            }
        }

        return preview;
    }

    private static void OnMissionEnded(Definitions defs, MissionEnded ended, IBusContext context)
    {
        IReadOnlyList<string> roster = context.View<IRosterView>().Roster;
        foreach (MissionTrait trait in ended.Result.Traits)
        {
            // Someone who died on the mission has left the roster by now; what the mission set on them, or toward them, goes with them.
            if ((trait.CrewId is not null && !roster.Contains(trait.CrewId)) || (trait.TargetId is not null && !roster.Contains(trait.TargetId)))
            {
                continue;
            }

            Change(defs, context, trait.Trait, trait.CrewId, trait.TargetId, trait.Amount, trait.Set, TraitCauses.Mission, trait.EncounterId);
        }
    }

    // Each stored trait in the order first set: one whose definition expires it, unchanged that many weeks, goes to 0; else one
    // whose definition steps it, unchanged that many weeks, changes by the step, which restarts its clock.
    private static void OnWeek(Definitions defs, WeekAdvanced advanced, IBusContext context)
    {
        TraitsState state = context.Module<TraitsState>();
        foreach (StoredTrait stored in state.Traits.ToList())
        {
            TraitDef def = defs[stored.Trait];
            int since = advanced.Week - stored.ChangedAtWeek;
            if (def.ExpiresWeeks is { } weeks && since >= weeks)
            {
                Change(defs, context, stored.Trait, stored.CrewId, stored.TargetId, 0, true, TraitCauses.Expired, null);
            }
            else if (def.Every is { } step && since >= step.Weeks)
            {
                Change(defs, context, stored.Trait, stored.CrewId, stored.TargetId, step.Add, false, TraitCauses.Weekly, null);
            }
        }
    }

    // A pair whose target died runs its onTargetDied first, once; then every trait on the dead or pointing at them goes to 0,
    // a pair the dead held with no rule (design 4.4).
    private static void OnCrewDied(Definitions defs, CrewDied died, IBusContext context)
    {
        TraitsState state = context.Module<TraitsState>();
        foreach (StoredTrait pair in state.Traits.Where(t => t.TargetId == died.CrewId).ToList())
        {
            Apply(defs, context, defs[pair.Trait], pair.CrewId!, pair.TargetId, defs[pair.Trait].OnTargetDied, TraitCauses.Death);
        }

        foreach (StoredTrait gone in state.Traits.Where(t => t.CrewId == died.CrewId || t.TargetId == died.CrewId).ToList())
        {
            Change(defs, context, gone.Trait, gone.CrewId, gone.TargetId, 0, true, TraitCauses.Death, null);
        }
    }

    // A rule's effects, in order, for one holder (and a pair's target): a trait change (its own trait, on the pair when it sits on
    // one, or another trait on the holder or the target), or loyalty and morale through the crew module. A target who is not on
    // the roster (one who has just died) is passed over.
    private static void Apply(Definitions defs, IBusContext context, TraitDef owner, string holderId, string? targetId, IReadOnlyList<TraitRuleEffect> effects, string cause)
    {
        IReadOnlyList<string> roster = context.View<IRosterView>().Roster;
        foreach (TraitRuleEffect effect in effects)
        {
            IEnumerable<string> who = effect.On switch
            {
                TraitRuleOn.Crew => roster,
                TraitRuleOn.Target when targetId is not null => [targetId],
                TraitRuleOn.Target => [],
                _ => [holderId],
            };
            foreach (string crewId in who.Where(roster.Contains).ToList())
            {
                if (effect.Loyalty != 0)
                {
                    context.Send(new AdjustLoyalty(crewId, effect.Loyalty));
                }
                else if (effect.Morale != 0)
                {
                    context.Send(new AdjustMorale(crewId, effect.Morale, MoraleCauses.Trait));
                }
                else if (effect.Trait is null && effect.On == TraitRuleOn.Holder)
                {
                    Change(defs, context, owner.Id, holderId, owner.OnPair ? targetId : null, effect.Amount, effect.Set, cause, null);
                }
                else
                {
                    Change(defs, context, effect.Trait ?? owner.Id, crewId, null, effect.Amount, effect.Set, cause, null);
                }
            }
        }
    }

    // Adds to or sets one trait, clamped to 0 and its definition's cap. A trait at 0 is not stored, and a change that changes
    // nothing is not published (and restarts no clock). encounterId null keeps the encounter that last changed the trait.
    private static void Change(Definitions defs, IBusContext context, string trait, string? crewId, string? targetId, int amount, bool set, string cause, string? encounterId)
    {
        TraitsState state = context.Module<TraitsState>();
        StoredTrait? stored = Find(state, trait, crewId, targetId);
        int from = stored?.Value ?? 0;
        int to = defs[trait].Clamp(set ? amount : from + amount);
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

    // The pack's definitions by id, and in their order; a trait the pack does not define has none of their rules.
    internal sealed class Definitions(IReadOnlyList<TraitDef> defs)
    {
        private readonly Dictionary<string, TraitDef> byId = defs.ToDictionary(d => d.Id);

        public IReadOnlyList<TraitDef> All => defs;

        public TraitDef this[string trait] => byId.GetValueOrDefault(trait) ?? TraitDef.Plain(trait);
    }
}

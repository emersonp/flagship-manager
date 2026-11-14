using Sim.Core;
using Sim.Core.Content;

namespace Sim.Missions;

public sealed record AssignTeam(string ObjectiveId, IReadOnlyList<string> CrewIds, string LeadId, IReadOnlyList<string> GearIds, Parameters Parameters) : ICommand;

public sealed record AnswerCall(string MissionId, string CallId, CallAnswer Answer) : ICommand;

[EventType("SiteArrived", 1)]
public sealed record SiteArrived(int Cycle, string AssignmentId, IReadOnlyList<SiteObjective> Objectives) : IEvent;

[EventType("TeamAssigned", 1)]
public sealed record TeamAssigned(string ObjectiveId, IReadOnlyList<string> CrewIds, string LeadId, IReadOnlyList<string> GearIds, Parameters Parameters) : IEvent;

[EventType("MissionBeat", 1)]
public sealed record MissionBeat(string MissionId, int Index, Beat Beat) : IEvent;

[EventType("ObjectiveUnattended", 1)]
public sealed record ObjectiveUnattended(string ObjectiveId, string TemplateId, int Roll, int Target, bool Succeeded) : IEvent;

public static class MissionsModule
{
    private const string ModuleName = "missions";

    // After crew, so the roster is current when a site is drawn or a mission ends.
    private const int Order = 200;

    public static void Register(EventBus bus, ContentPack pack)
    {
        bus.Module<MissionsState>(ModuleName);
        bus.Guard<AssignTeam>(Order, ModuleName, (command, context) => CheckAssign(pack, command, context));
        bus.Guard<AnswerCall>(Order, ModuleName, (command, context) => CheckAnswer(command, context));
        bus.Guard<AdvancePhase>(Order, ModuleName, (_, context) => IsWaiting(context) ? "A call is waiting for an answer." : null);
        bus.Handle<AssignTeam>(OnAssign);
        bus.Handle<AnswerCall>(OnAnswer);
        bus.Subscribe<PhaseEntered>(Order, ModuleName, (e, context) => OnPhase(pack, e, context));
    }

    private static bool IsWaiting(IBusContext context)
    {
        return context.State.Phase == CyclePhase.Resolution && context.Module<MissionsState>().Site?.Mission?.State.Pending is not null;
    }

    private static string? CheckAssign(ContentPack pack, AssignTeam command, IBusContext context)
    {
        if (command.ObjectiveId is null)
        {
            return "The command has no ObjectiveId.";
        }

        if (command.CrewIds is null)
        {
            return "The command has no CrewIds.";
        }

        if (command.LeadId is null)
        {
            return "The command has no LeadId.";
        }

        if (command.GearIds is null)
        {
            return "The command has no GearIds.";
        }

        if (command.Parameters is null)
        {
            return "The command has no Parameters.";
        }

        SiteState? site = context.Module<MissionsState>().Site;
        if (site is null)
        {
            return "No site has arrived.";
        }

        if (context.State.Phase != CyclePhase.Briefing)
        {
            return "A team can only be assigned in the briefing.";
        }

        if (site.Briefing is not null)
        {
            return "A team is already assigned this cycle.";
        }

        SiteObjective? objective = site.Objectives.FirstOrDefault(o => o.Id == command.ObjectiveId);
        if (objective is null)
        {
            return $"Objective '{command.ObjectiveId}' is not at this site.";
        }

        foreach ((string name, int value) in new[] { ("Priority", command.Parameters.Priority), ("Risk", command.Parameters.Risk), ("Autonomy", command.Parameters.Autonomy) })
        {
            if (value is < -1 or > 1)
            {
                return $"{name} {value} is outside -1 to 1.";
            }
        }

        ObjectiveDef template = pack.Objectives.Single(o => o.Id == objective.TemplateId);
        int size = command.CrewIds.Count;
        if (size < template.TeamSizeMin || size > template.TeamSizeMax)
        {
            return $"A team of {size} is outside the {template.TeamSizeMin} to {template.TeamSizeMax} this objective takes.";
        }

        IRosterView roster = context.View<IRosterView>();
        var seen = new HashSet<string>();
        foreach (string id in command.CrewIds)
        {
            if (!seen.Add(id))
            {
                return $"Crew member '{id}' is listed twice.";
            }

            if (!roster.Roster.Contains(id))
            {
                return $"Crew member '{id}' is not on the roster.";
            }

            if (!roster.IsFit(id))
            {
                return $"Crew member '{id}' is not fit.";
            }
        }

        if (!seen.Contains(command.LeadId))
        {
            return $"The lead '{command.LeadId}' is not on the team.";
        }

        return CheckGear(pack, site, command);
    }

    private static string? CheckGear(ContentPack pack, SiteState site, AssignTeam command)
    {
        int limit = pack.Assignments.Single(a => a.Id == site.AssignmentId).LoadoutLimit;
        var seen = new HashSet<string>();
        foreach (string id in command.GearIds)
        {
            if (pack.Gear.All(g => g.Id != id))
            {
                return $"Gear '{id}' is unknown.";
            }

            if (!seen.Add(id))
            {
                return $"Gear '{id}' is listed twice.";
            }
        }

        return command.GearIds.Count > limit ? $"The loadout holds at most {limit} items." : null;
    }

    private static void OnAssign(AssignTeam command, IBusContext context)
    {
        context.Module<MissionsState>().Site!.Briefing = new Briefing(command.ObjectiveId, command.CrewIds.ToList(), command.LeadId, command.GearIds.ToList(), command.Parameters);
        context.Publish(new TeamAssigned(command.ObjectiveId, command.CrewIds.ToList(), command.LeadId, command.GearIds.ToList(), command.Parameters));
    }

    private static string? CheckAnswer(AnswerCall command, IBusContext context)
    {
        MissionInFlight? mission = context.Module<MissionsState>().Site?.Mission;
        if (mission?.State.Pending is not { } pending)
        {
            return "No mission is waiting for an answer.";
        }

        if (mission.Id != command.MissionId)
        {
            return $"The waiting mission is '{mission.Id}', not '{command.MissionId}'.";
        }

        if (pending.CallId != command.CallId)
        {
            return $"The pending call is '{pending.CallId}', not '{command.CallId}'.";
        }

        return pending.Options.Contains(command.Answer) ? null : $"'{command.Answer}' is not an option for {pending.DecisionId}.";
    }

    private static void OnAnswer(AnswerCall command, IBusContext context)
    {
        MissionInFlight mission = context.Module<MissionsState>().Site!.Mission!;
        Run(mission, MissionResolver.Advance(mission.State, new CallReply(command.CallId, command.Answer)), context);
    }

    private static void OnPhase(ContentPack pack, PhaseEntered entered, IBusContext context)
    {
        switch (entered.Phase)
        {
            case CyclePhase.Orders:
                DrawSite(pack, entered.Cycle, context);
                break;
            case CyclePhase.Resolution:
                StartMission(pack, entered.Cycle, context);
                break;
            case CyclePhase.Aftermath:
                RollUnattended(pack, context);
                break;
        }
    }

    private static void DrawSite(ContentPack pack, int cycle, IBusContext context)
    {
        AssignmentDef assignment = pack.Assignments[0];
        int total = assignment.Offer.Values.Sum();
        int pick = context.Rng("site:offer").Next(total);
        int count = 0;
        foreach ((int offered, int weight) in assignment.Offer.OrderBy(o => o.Key))
        {
            pick -= weight;
            if (pick < 0)
            {
                count = offered;
                break;
            }
        }

        List<ObjectiveDef> candidates = pack.Objectives
            .Where(o => assignment.ObjectiveTags.All(o.Tags.Contains))
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .ToList();
        var objectives = new List<SiteObjective>();
        for (int n = 1; n <= count; n++)
        {
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException($"Assignment '{assignment.Id}' offers {count} objectives, but only {n - 1} templates match.");
            }

            ObjectiveDef drawn = candidates[context.Rng($"site:draw:{n}").Next(candidates.Count)];
            candidates.Remove(drawn);
            objectives.Add(new SiteObjective($"o_{cycle}_{n}", drawn.Id));
        }

        context.Module<MissionsState>().Site = new SiteState
        {
            Cycle = cycle,
            AssignmentId = assignment.Id,
            Objectives = objectives,
            Briefing = null,
            Mission = null,
        };
        context.Publish(new SiteArrived(cycle, assignment.Id, objectives.ToList()));
    }

    private static void StartMission(ContentPack pack, int cycle, IBusContext context)
    {
        if (context.Module<MissionsState>().Site is not { } site || site.Briefing is not { } briefing)
        {
            return;
        }

        string id = $"m_{cycle}_1";
        SiteObjective objective = site.Objectives.Single(o => o.Id == briefing.ObjectiveId);
        ObjectiveDef template = pack.Objectives.Single(o => o.Id == objective.TemplateId);
        IRosterView roster = context.View<IRosterView>();
        List<TeamMember> members = briefing.CrewIds
            .Select(c => roster.Member(c) ?? throw new InvalidOperationException($"Crew member '{c}' is no longer available for mission {id}."))
            .ToList();
        List<string> gearTags = briefing.GearIds.Select(g => pack.Gear.Single(d => d.Id == g).Tag).ToList();
        ulong seed = context.Rng($"mission:{id}").NextUInt64();

        context.Publish(new MissionStarted(id, objective.Id, template.Id, briefing.CrewIds, briefing.LeadId));
        MissionState state = MissionResolver.Start(template, pack.Encounters, new Team(members, briefing.LeadId), gearTags, briefing.Parameters, pack.Tuning, seed);
        var mission = new MissionInFlight { Id = id, ObjectiveId = objective.Id, State = state, BeatCount = 0 };
        site.Mission = mission;
        Run(mission, MissionResolver.Advance(state), context);
    }

    private static void Run(MissionInFlight mission, MissionStep step, IBusContext context)
    {
        mission.State = step.State;
        foreach (Beat beat in step.Beats)
        {
            context.Publish(new MissionBeat(mission.Id, mission.BeatCount++, beat));
        }

        if (step.Result is { } result)
        {
            context.Publish(new MissionEnded(mission.Id, mission.ObjectiveId, result));
        }
    }

    private static void RollUnattended(ContentPack pack, IBusContext context)
    {
        if (context.Module<MissionsState>().Site is not { } site)
        {
            return;
        }

        foreach (SiteObjective objective in site.Objectives.Where(o => site.Briefing?.ObjectiveId != o.Id))
        {
            if (pack.Objectives.Single(o => o.Id == objective.TemplateId).UnattendedDifficulty is not { } difficulty)
            {
                continue;
            }

            int roll = pack.Tuning.CheckDice.Roll(context.Rng($"unattended:{objective.Id}"));
            int target = difficulty + pack.Tuning.CheckTarget;
            context.Publish(new ObjectiveUnattended(objective.Id, objective.TemplateId, roll, target, roll >= target));
        }
    }
}

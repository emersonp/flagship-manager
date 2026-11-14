using GameHost;
using MissionBench;
using Sim.Core;
using Sim.Core.Content;
using Sim.Cycle;
using Sim.Missions;

namespace SimRunner;

// A simple captain: one command at a time, the next thing a sensible player would do.
public static class AutoCaptain
{
    public static IReadOnlyList<PublishedEvent> Step(Game game, LegacyState state, ContentPack pack)
    {
        return game.Bus.Send(state, NextCommand(game, state, pack));
    }

    // Steps until the cycle number changes.
    public static IReadOnlyList<PublishedEvent> PlayCycle(Game game, LegacyState state, ContentPack pack)
    {
        int cycle = state.Cycle;
        var events = new List<PublishedEvent>();
        while (state.Cycle == cycle)
        {
            events.AddRange(Step(game, state, pack));
        }

        return events;
    }

    private static ICommand NextCommand(Game game, LegacyState state, ContentPack pack)
    {
        // The state of a legacy that is not founded has no modules yet, and a read must not create one.
        if (!state.TryModule(CycleModule.Name, out CycleState? cycle) || !cycle.Founded)
        {
            return new FoundLegacy();
        }

        SiteState? site = state.TryModule(MissionsModule.Name, out MissionsState? missions) ? missions.Site : null;
        if (state.Phase == CyclePhase.Briefing && site is { Briefing: null } && ChooseTeam(game, state, pack, site) is { } assign)
        {
            return assign;
        }

        if (state.Phase == CyclePhase.Resolution && site?.Mission is { } mission && mission.State.Pending is { } call)
        {
            return new AnswerCall(mission.Id, call.CallId, Bench.Answer(CallPolicy.BackLead, call));
        }

        return new AdvancePhase();
    }

    private static AssignTeam? ChooseTeam(Game game, LegacyState state, ContentPack pack, SiteState site)
    {
        SiteObjective objective = site.Objectives[0];
        ObjectiveDef template = pack.Objectives.Single(o => o.Id == objective.TemplateId);
        IRosterView roster = game.Bus.View<IRosterView>(state);
        List<TeamMember> fit = roster.Roster.Where(roster.IsFit).Select(id => roster.Member(id)!).ToList();
        if (fit.Count < template.TeamSizeMin)
        {
            return null;
        }

        // The sorts are stable, so ties keep roster order.
        List<TeamMember> team = fit.OrderByDescending(m => m.Skills.Values.Sum()).Take(template.TeamSizeMin).ToList();
        TeamMember lead = fit.Where(team.Contains).OrderByDescending(m => m.Seniority).First();
        int loadout = pack.Assignments.Single(a => a.Id == site.AssignmentId).LoadoutLimit;
        string[] gear = pack.Gear.Take(loadout).Select(g => g.Id).ToArray();
        return new AssignTeam(objective.Id, team.Select(m => m.Id).ToList(), lead.Id, gear, new Parameters());
    }
}

using GameHost;
using MissionBench;
using Sim.Core;
using Sim.Core.Content;
using Sim.Cycle;
using Sim.Missions;
using Sim.Storylets;

namespace SimRunner;

// How the captain answers storylets and vignettes (1e part three, design 4.10). First: each storylet's first option, and each
// vignette left to its default. Random: an option of each, picked by a hash of the legacy's seed and the offer, so it draws from no
// game stream and needs no stored state.
public enum AnswerPolicy
{
    First,
    Random,
}

// A simple captain: one command at a time, the next thing a sensible player would do.
public static class AutoCaptain
{
    // The captain's own stream (design 5.6): never one the game draws from.
    public const string Stream = "sim-captain";

    public static IReadOnlyList<PublishedEvent> Step(Game game, LegacyState state, ContentPack pack, AnswerPolicy answers = AnswerPolicy.First)
    {
        return game.Bus.Send(state, NextCommand(game, state, pack, answers));
    }

    // Steps until the cycle number changes.
    public static IReadOnlyList<PublishedEvent> PlayCycle(Game game, LegacyState state, ContentPack pack, AnswerPolicy answers = AnswerPolicy.First)
    {
        int cycle = state.Cycle;
        var events = new List<PublishedEvent>();
        while (state.Cycle == cycle)
        {
            events.AddRange(Step(game, state, pack, answers));
        }

        return events;
    }

    // What the captain would send next; the web parity test posts the equivalent request.
    public static ICommand NextCommand(Game game, LegacyState state, ContentPack pack, AnswerPolicy answers = AnswerPolicy.First)
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

        if (state.Phase == CyclePhase.Itinerary && Answer(state, pack, answers) is { } answer)
        {
            return answer;
        }

        return new AdvancePhase();
    }

    // The first waiting storylet, answered by the policy. One the pack no longer has cannot be answered and does not hold the
    // itinerary, so it is passed over. Under random, each waiting vignette with a choice is answered too; under first, they are left
    // to their defaults.
    private static ICommand? Answer(LegacyState state, ContentPack pack, AnswerPolicy answers)
    {
        if (!state.TryModule(StoryletsModule.Name, out StoryletsState? storylets))
        {
            return null;
        }

        foreach (WaitingStorylet waiting in storylets.Waiting)
        {
            if (pack.Storylets.FirstOrDefault(s => s.Id == waiting.StoryletId) is { } storylet)
            {
                return new AnswerStorylet(waiting.OfferId, storylet.Options[Pick(state, waiting.OfferId, storylet.Options.Count, answers)].Id);
            }
        }

        if (answers == AnswerPolicy.Random)
        {
            foreach (WaitingVignette waiting in storylets.Vignettes)
            {
                if (pack.Vignettes.FirstOrDefault(v => v.Id == waiting.VignetteId) is { Options.Count: > 0 } vignette)
                {
                    return new AnswerVignette(waiting.OfferId, vignette.Options[Pick(state, waiting.OfferId, vignette.Options.Count, answers)].Id);
                }
            }
        }

        return null;
    }

    // An index from the legacy's seed and the offer: the same legacy and offer always pick the same option.
    internal static int Pick(LegacyState state, string offerId, int count, AnswerPolicy answers)
    {
        return answers == AnswerPolicy.First ? 0 : RngStream.For(state.Seed, 0, $"{Stream}:{offerId}").Next(count);
    }

    // The fittest by total skill, as many as the objective's smallest team. The captain keeps promises (design 4.10): a crew member
    // whose traits would cost something if sent (a sent rule, or a withTarget rule with their target) is left out unless the smallest
    // team cannot then be fielded, when they are added back in roster order.
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

        HashSet<string> promised = game.Bus.View<ITraitView>(state).Preview(roster.Roster)
            .Where(p => p.When is LaunchWhen.Sent or LaunchWhen.WithTarget)
            .Select(p => p.HolderId)
            .ToHashSet();
        List<TeamMember> free = fit.Where(m => !promised.Contains(m.Id)).ToList();
        List<TeamMember> pool = free.Count >= template.TeamSizeMin ? free : [.. free, .. fit.Where(m => promised.Contains(m.Id)).Take(template.TeamSizeMin - free.Count)];

        // The sorts are stable, so ties keep roster order.
        List<TeamMember> team = pool.OrderByDescending(m => m.Skills.Values.Sum()).Take(template.TeamSizeMin).ToList();
        TeamMember lead = fit.Where(team.Contains).OrderByDescending(m => m.Seniority).First();
        int loadout = pack.Assignments.Single(a => a.Id == site.AssignmentId).LoadoutLimit;
        string[] gear = pack.Gear.Take(loadout).Select(g => g.Id).ToArray();
        return new AssignTeam(objective.Id, team.Select(m => m.Id).ToList(), lead.Id, gear, new Parameters());
    }
}

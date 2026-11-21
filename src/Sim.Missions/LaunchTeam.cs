using Sim.Core;
using Sim.Core.Content;

namespace Sim.Missions;

// The team the launch sends, and the launch's checks on who can be in it (slice 1f, design 4.5): lifted out of MissionsModule so the
// bench builds a real team with the launch's own code. It reads the views and nothing else, so it can be called on any state the
// modules' views serve: a legacy read for the bench, or a test ship founded in memory (slice 1f-previews).
public static class LaunchTeam
{
    // The team the launch sends: each member from the roster view (skills less injuries, morale bonus), with their own traits and this
    // team's trait bonuses from the trait view, and the ship's traits. The launch calls it before MissionStarted, so the traits are the
    // ones each member walks on board with, before the launch rules run (which change crew, never the ship). Throws for a member the
    // roster no longer has: Refusal is the check that comes first.
    public static Team Build(IRosterView roster, ITraitView traits, IReadOnlyList<string> crewIds, string leadId)
    {
        List<TeamMember> members = crewIds
            .Select(c => roster.Member(c) ?? throw new InvalidOperationException($"Crew member '{c}' is no longer available."))
            .Select(m => m with { Traits = Own(traits.Held(m.Id)), TraitBonuses = traits.Bonuses(m.Id, crewIds) is { Count: > 0 } b ? b : null })
            .ToList();
        return new Team(members, leadId, Own(traits.Held(null)));
    }

    // Why the launch would refuse this member, or null: not on the roster, or not fit (AssignTeam's check, in its words).
    public static string? Refusal(IRosterView roster, string crewId)
    {
        if (!roster.Roster.Contains(crewId))
        {
            return $"Crew member '{crewId}' is not on the roster.";
        }

        if (!roster.IsFit(crewId))
        {
            return $"Crew member '{crewId}' is not fit.";
        }

        return null;
    }

    // Why the launch would refuse a team of this size for this objective, or null (AssignTeam's check, in its words).
    public static string? SizeRefusal(ObjectiveDef objective, int size)
    {
        return size < objective.TeamSizeMin || size > objective.TeamSizeMax
            ? $"A team of {size} is outside the {objective.TeamSizeMin} to {objective.TeamSizeMax} this objective takes."
            : null;
    }

    // One's own traits by id, the pairs held left out; null when there are none.
    private static Dictionary<string, int>? Own(IReadOnlyList<TraitHeld> held)
    {
        Dictionary<string, int> own = held.Where(t => t.TargetId is null).ToDictionary(t => t.Trait, t => t.Value);
        return own.Count == 0 ? null : own;
    }
}

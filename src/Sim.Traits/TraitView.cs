using Sim.Core;
using Sim.Core.Content;

namespace Sim.Traits;

internal sealed class TraitView(TraitsState state, TraitsModule.Definitions defs) : ITraitView
{
    public int Value(string trait, string? crewId, string? targetId = null)
    {
        return TraitsModule.Find(state, trait, crewId, targetId)?.Value ?? 0;
    }

    public IReadOnlyList<TraitHeld> Held(string? crewId)
    {
        return state.Traits.Where(t => t.CrewId == crewId).Select(t => new TraitHeld(t.Trait, t.TargetId, t.Value, t.ChangedAtDay, t.ChangedAtCycle, t.EncounterId)).ToList();
    }

    public string? EncounterId(string trait, string? crewId, string? targetId = null)
    {
        return TraitsModule.Find(state, trait, crewId, targetId)?.EncounterId;
    }

    // One entry for each trait with a check bonus that holds for this member on this team: a pair's with withTarget only when its
    // target is on the team too. Two pairs of one trait that both hold are one entry, their bonuses summed.
    public IReadOnlyList<TraitBonus> Bonuses(string crewId, IReadOnlyCollection<string> team)
    {
        var bonuses = new List<TraitBonus>();
        foreach (IGrouping<string, StoredTrait> held in state.Traits.Where(t => t.CrewId == crewId).GroupBy(t => t.Trait))
        {
            if (defs[held.Key].Mission is not { } mission)
            {
                continue;
            }

            int count = held.Count(t => !mission.WithTarget || (t.TargetId is not null && team.Contains(t.TargetId)));
            if (count > 0)
            {
                bonuses.Add(new TraitBonus(held.Key, mission.CheckBonus * count));
            }
        }

        return bonuses;
    }

    public IReadOnlyList<LaunchPreview> Preview(IReadOnlyList<string> roster)
    {
        return TraitsModule.Preview(state, defs, roster);
    }
}

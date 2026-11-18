using Sim.Core;

namespace Sim.Traits;

internal sealed class TraitView(TraitsState state) : ITraitView
{
    public int Value(string trait, string? crewId, string? targetId = null)
    {
        return TraitsModule.Find(state, trait, crewId, targetId)?.Value ?? 0;
    }

    public IReadOnlyList<TraitHeld> Held(string? crewId)
    {
        return state.Traits.Where(t => t.CrewId == crewId).Select(t => new TraitHeld(t.Trait, t.TargetId, t.Value, t.ChangedAtWeek, t.EncounterId)).ToList();
    }

    public string? EncounterId(string trait, string? crewId, string? targetId = null)
    {
        return TraitsModule.Find(state, trait, crewId, targetId)?.EncounterId;
    }
}

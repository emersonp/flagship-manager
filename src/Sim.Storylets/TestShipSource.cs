using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;

namespace Sim.Storylets;

// A test ship as the expressions read it (1e part six, design 4.6), beside StateSource and DryRun's Rewound: the editor casts
// through exactly the interface the module casts the game through. Each reading is the ship as written; anyone not listed is as the
// starting crew file has them, at the tuning file's starting morale and loyalty, fit and aboard. Times are counted back from the
// ship's today.
public sealed class TestShipSource : IStateSource
{
    private readonly ContentPack pack;
    private readonly TestShipDef ship;
    private readonly Dictionary<string, TestShipCrew> changes;

    public TestShipSource(ContentPack pack, TestShipDef ship)
    {
        this.pack = pack;
        this.ship = ship;
        changes = ship.Crew.ToDictionary(c => c.Id, StringComparer.Ordinal);
        Roster = pack.Crew.Select(c => c.Id).Where(id => Change(id)?.Alive != false).ToList();
    }

    // The living crew, in the starting crew file's order: who casting draws from, as the module's roster is.
    public IReadOnlyList<string> Roster { get; }

    public int Cycle => ship.Cycle;

    public int Morale(string crewId)
    {
        return Change(crewId)?.Morale ?? pack.CrewTuning.MoraleResting;
    }

    public int Loyalty(string crewId)
    {
        return Change(crewId)?.Loyalty ?? pack.CrewTuning.StartingLoyalty;
    }

    public int Skill(string crewId, string skill)
    {
        if (Change(crewId)?.Skills is { } skills && skills.TryGetValue(skill, out int level))
        {
            return level;
        }

        return pack.Crew.FirstOrDefault(c => c.Id == crewId)?.Skills.GetValueOrDefault(skill) ?? 0;
    }

    public bool IsFit(string crewId)
    {
        return Change(crewId)?.Fit ?? true;
    }

    // Alive and not away.
    public bool IsAboard(string crewId)
    {
        return Roster.Contains(crewId) && Change(crewId)?.Aboard != false;
    }

    public int Trait(string trait, string? crewId, string? targetId = null)
    {
        return Held(trait, crewId, targetId)?.Value ?? 0;
    }

    // Cycles among the last `cycles` in which it happened: each listed age from 1 to the window.
    public int History(string crewId, string kind, int cycles)
    {
        return ship.History.TryGetValue(crewId, out IReadOnlyDictionary<string, IReadOnlyList<int>>? kinds) && kinds.TryGetValue(kind, out IReadOnlyList<int>? ages)
            ? ages.Count(a => a >= 1 && a <= cycles)
            : 0;
    }

    public Since? StoryletSince(string storylet)
    {
        return ship.Storylets.FirstOrDefault(s => s.Storylet == storylet) is { } arrival ? new Since(arrival.CyclesAgo, arrival.DaysAgo) : null;
    }

    // On one crew member or the ship, never a pair's, as StateSource reads it.
    public Since? TraitSince(string trait, string? crewId)
    {
        return Held(trait, crewId, null) is { } held ? new Since(held.CyclesAgo, held.DaysAgo) : null;
    }

    // Within the last `days` days, today (0 days ago) included, as IChronicleView.Count counts.
    public int ChronicleCount(string kind, int days)
    {
        return ship.Chronicle.Count(e => e.Kind == kind && e.DaysAgo < days);
    }

    public int ChronicleCount(string crewId, string kind, int days)
    {
        return ship.Chronicle.Count(e => e.Kind == kind && e.DaysAgo < days && e.Subjects.Contains(crewId));
    }

    private TestShipCrew? Change(string crewId)
    {
        return changes.GetValueOrDefault(crewId);
    }

    private TestShipTrait? Held(string trait, string? crewId, string? targetId)
    {
        return ship.Traits.FirstOrDefault(t => t.Trait == trait && t.On == crewId && t.Toward == targetId && t.Value > 0);
    }
}

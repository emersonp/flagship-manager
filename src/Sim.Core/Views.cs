namespace Sim.Core;

// Read-only crew roster for modules that must not reference the crew module.
public interface IRosterView
{
    // Living crew ids in roster order.
    IReadOnlyList<string> Roster { get; }

    bool IsFit(string crewId);

    // Null for an unknown or dead crew member.
    TeamMember? Member(string crewId);

    // A living crew member's stats, read by storylet requirements (1e part two); 0 for an unknown or dead one.
    int Morale(string crewId);

    int Loyalty(string crewId);
}

// Read-only traits (slice 1e, part three) for modules that must not reference the traits module. CrewId null: the ship.
public interface ITraitView
{
    // 0 when it is not held.
    int Value(string trait, string? crewId, string? targetId = null);

    // The ship's traits (crewId null), or a crew member's, pairs they hold included, in the order first set.
    IReadOnlyList<TraitHeld> Held(string? crewId);

    // The encounter that last changed it, or null.
    string? EncounterId(string trait, string? crewId, string? targetId = null);
}

namespace Sim.Core;

// The resolver's whole view of a crew member. Caution and discipline run from -2 to +2. MoraleBonus is added to every check
// the member makes; the crew module works it out from their morale, which missions never read. Traits and TraitBonuses (1e part
// three) are fixed at launch from the trait view: the member's own traits (not the pairs they hold) for approach requirements, and
// each trait's check bonus, added to every check the member makes and named in its beat.
public sealed record TeamMember(string Id, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline, int MoraleBonus = 0,
    IReadOnlyDictionary<string, int>? Traits = null, IReadOnlyList<TraitBonus>? TraitBonuses = null);

// ShipTraits (1e part three): the ship's traits at launch, for approach requirements on the ship.
public sealed record Team(IReadOnlyList<TeamMember> Members, string LeadId, IReadOnlyDictionary<string, int>? ShipTraits = null);

// One trait's check bonus for one member on one mission (1e part three).
public sealed record TraitBonus(string Trait, int Amount);

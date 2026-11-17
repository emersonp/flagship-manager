namespace Sim.Core;

// The resolver's whole view of a crew member. Caution and discipline run from -2 to +2. MoraleBonus is added to every check
// the member makes; the crew module works it out from their morale, which missions never read.
public sealed record TeamMember(string Id, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline, int MoraleBonus = 0);

public sealed record Team(IReadOnlyList<TeamMember> Members, string LeadId);

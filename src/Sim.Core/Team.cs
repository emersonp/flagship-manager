namespace Sim.Core;

// The resolver's whole view of a crew member. Caution and discipline run from -2 to +2.
public sealed record TeamMember(string Id, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline);

public sealed record Team(IReadOnlyList<TeamMember> Members, string LeadId);

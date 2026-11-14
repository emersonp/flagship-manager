namespace Sim.Core;

// Read-only crew roster for modules that must not reference the crew module.
public interface IRosterView
{
    // Living crew ids in roster order.
    IReadOnlyList<string> Roster { get; }

    bool IsFit(string crewId);

    // Null for an unknown or dead crew member.
    TeamMember? Member(string crewId);
}

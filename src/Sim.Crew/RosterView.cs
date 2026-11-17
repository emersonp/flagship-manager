using Sim.Core;
using Sim.Core.Content;

namespace Sim.Crew;

internal sealed class RosterView(CrewState state, CrewTuning tuning) : IRosterView
{
    public IReadOnlyList<string> Roster => state.Members.Where(m => m.Status != CrewStatus.Dead).Select(m => m.Id).ToList();

    public bool IsFit(string crewId)
    {
        return Find(crewId)?.Status == CrewStatus.Active;
    }

    public TeamMember? Member(string crewId)
    {
        CrewMember? member = Find(crewId);
        if (member is null || member.Status == CrewStatus.Dead)
        {
            return null;
        }

        int injuries = member.LightInjuriesHealAtWeek.Count;
        Dictionary<string, int> skills = member.Skills.ToDictionary(s => s.Key, s => Math.Max(0, s.Value.Level - injuries));
        return new TeamMember(member.Id, member.Seniority, skills, member.Caution, member.Discipline, tuning.MoraleBonus(member.Morale));
    }

    private CrewMember? Find(string crewId)
    {
        return state.Members.FirstOrDefault(m => m.Id == crewId);
    }
}

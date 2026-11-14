using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench;

public static class TeamGenerator
{
    // The first member is the lead and the most senior. Keyed by seed, so the same seed gives the same team under any tuning of the resolver.
    public static Team Generate(BenchTuning tuning, TeamStrength strength, LeadStyle lead, IReadOnlyList<SkillDef> skills, ulong seed)
    {
        BenchTeamDef def = strength switch
        {
            TeamStrength.Rookie => tuning.Rookie,
            TeamStrength.Veteran => tuning.Veteran,
            _ => tuning.Mixed,
        };
        (int caution, int discipline) = lead switch
        {
            LeadStyle.Cautious => (2, 1),
            LeadStyle.Reckless => (-2, -1),
            _ => (0, 0),
        };

        var members = new List<TeamMember>();
        for (int i = 0; i < def.Size; i++)
        {
            (int min, int max) = i < def.Seniors ? (def.SeniorSkillMin, def.SeniorSkillMax) : (def.SkillMin, def.SkillMax);
            Dictionary<string, int> levels = skills.ToDictionary(s => s.Id, s => min + RngStream.For(seed, 0, $"team:{i}:{s.Id}").Next(max - min + 1));
            members.Add(new TeamMember(
                $"CREW_{i + 1:00}",
                def.Size - i,
                levels,
                i == 0 ? caution : RngStream.For(seed, 0, $"team:{i}:caution").Next(3) - 1,
                i == 0 ? discipline : RngStream.For(seed, 0, $"team:{i}:discipline").Next(3) - 1));
        }

        return new Team(members, members[0].Id);
    }
}

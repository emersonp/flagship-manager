using Sim.Missions;

namespace Sim.Tests;

// The eight placeholder crew the mission numbers were first benched on (2026-11-13), and a way to play a whole mission.
internal static partial class TestContent
{
    public static readonly string[] Gear = ["scanner", "tools"];

    private static readonly Dictionary<int, (int Seniority, int[] Skills, int Caution, int Discipline)> Crew = new()
    {
        [1] = (5, [6, 5, 4, 3], 2, 2),
        [2] = (4, [4, 6, 5, 2], -2, -1),
        [3] = (4, [5, 3, 6, 5], 0, 1),
        [4] = (3, [3, 4, 3, 6], 1, -2),
        [5] = (2, [2, 3, 2, 3], -1, 0),
        [6] = (2, [3, 1, 3, 2], 1, 1),
        [7] = (1, [1, 2, 1, 2], 0, 0),
        [8] = (1, [2, 1, 2, 1], -2, 2),
    };

    public static Team Team(int lead, params int[] members)
    {
        return new Team(members.Select(Member).ToList(), $"CREW_{lead:00}");
    }

    public static TeamMember Member(int n)
    {
        (int seniority, int[] skills, int caution, int discipline) = Crew[n];
        var levels = new Dictionary<string, int> { ["SKILL_A"] = skills[0], ["SKILL_B"] = skills[1], ["SKILL_C"] = skills[2], ["SKILL_D"] = skills[3] };
        return new TeamMember($"CREW_{n:00}", seniority, levels, caution, discipline);
    }

    // Starts a mission on the starter pack and plays it to the end, answering every call the same way.
    public static (MissionState Start, List<Beat> Beats, MissionResult Result) Play(Team team, ulong seed, Parameters? parameters = null, CallAnswer? answer = null)
    {
        MissionState start = MissionResolver.Start(Objective, Pack.Encounters, team, Gear, parameters ?? new Parameters(), Tuning, seed);
        var beats = new List<Beat>();
        MissionStep step = MissionResolver.Advance(start);
        beats.AddRange(step.Beats);
        while (step.Pending is { } call)
        {
            step = MissionResolver.Advance(step.State, answer ?? call.LeadPrefers);
            beats.AddRange(step.Beats);
        }

        return (start, beats, step.Result!);
    }
}

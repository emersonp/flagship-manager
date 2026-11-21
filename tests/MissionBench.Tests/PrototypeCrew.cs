using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench.Tests;

// The eight placeholder crew the mission numbers were first benched on (2026-11-13).
internal static class PrototypeCrew
{
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

    public static readonly string[] Gear = ["scanner", "tools"];

    public static ContentPack Pack { get; } = LoadPack();

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

    // The repository's root, found by its Directory.Build.props.
    public static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static ContentPack LoadPack()
    {
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(Root(), "tests", "fixtures", "content"));
        return result.Pack ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
    }
}

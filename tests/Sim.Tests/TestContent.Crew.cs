namespace Sim.Tests;

// Pack fixtures: the repository's files as a dictionary, and a way to make a partial pack load.
internal static partial class TestContent
{
    private const string FixtureCrew = "- { id: FIXTURE_CREW, seniority: 1, skills: {}, caution: 0, discipline: 0 }\n";

    private const string FixtureAssignment = "- { id: FIXTURE_ASSIGNMENT, objectives: { tags: [] }, offer: { 1: 1 }, loadoutLimit: 0 }\n";

    private const string FixtureSkill = "- id: FIXTURE_SKILL\n";

    private const string FixtureEncounter = """
        - id: FIXTURE_ENCOUNTER
          tags: [fixture]
          approaches:
            careful: { skill: FIXTURE_SKILL, difficulty: 5 }
            fast: { skill: FIXTURE_SKILL, difficulty: 6 }

        """;

    private const string FixtureObjective = """
        - id: FIXTURE_OBJECTIVE
          teamSize: 1
          need: 1
          stages:
            - { slot: approach, draw: { tags: [fixture] }, count: 1 }

        """;

    public static Dictionary<string, string> RepoFiles()
    {
        string content = Path.Combine(Root, "tests", "fixtures", "content");
        return Directory
            .EnumerateFiles(content, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(content, f).Replace('\\', '/'), File.ReadAllText);
    }

    // Adds the crew, gear, assignment, and crew tuning files a pack now needs, so a test of something else still loads.
    // The crew and assignment are fixtures with ids of their own, because the repository's would clash with the skills
    // and objectives a test defines. A test with no objectives of its own also gets one, since an assignment needs a match.
    public static Dictionary<string, string> WithCrewFiles(Dictionary<string, string> files)
    {
        Dictionary<string, string> repo = RepoFiles();
        files.TryAdd("crew/fixture.yaml", FixtureCrew);
        files.TryAdd("assignments/fixture.yaml", FixtureAssignment);
        files.TryAdd("gear/base.yaml", repo["gear/base.yaml"]);
        files.TryAdd("tuning/crew.yaml", repo["tuning/crew.yaml"]);
        if (!files.Keys.Any(k => k.StartsWith("objectives/", StringComparison.Ordinal)))
        {
            files["skills/fixture.yaml"] = FixtureSkill;
            files["encounters/fixture.yaml"] = FixtureEncounter;
            files["objectives/fixture.yaml"] = FixtureObjective;
        }

        return files;
    }
}

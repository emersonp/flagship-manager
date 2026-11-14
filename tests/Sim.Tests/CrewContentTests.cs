using Sim.Content;
using Sim.Core;
using Sim.Core.Content;

namespace Sim.Tests;

public class CrewContentTests
{
    [Fact]
    public void The_starter_pack_has_the_crew_gear_assignment_and_tuning()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Equal(8, pack.Crew.Count);
        Assert.Equal(4, pack.Gear.Count);
        Assert.Equal(new[] { "scanner", "tools", "comms", "medical" }, pack.Gear.Select(g => g.Tag));
        AssignmentDef assignment = Assert.Single(pack.Assignments);
        Assert.Equal("ASSIGNMENT_TEMPLATE_1", assignment.Id);
        Assert.Empty(assignment.ObjectiveTags);
        Assert.Equal(new Dictionary<int, int> { [1] = 6, [2] = 3 }, assignment.Offer);
        Assert.Equal(2, assignment.LoadoutLimit);
        Assert.Equal(
            new CrewTuning { XpBase = 10, MaxLevel = 10, LightInjuryWeeks = 2, IncapacitatedWeeks = 6, StartingLoyalty = 50, LoyaltyPerPoint = 2, LoyaltyMin = 0, LoyaltyMax = 100 },
            pack.CrewTuning);
        Assert.Equal(new int?[] { 6, 5 }, pack.Objectives.Select(o => o.UnattendedDifficulty));
    }

    // The bench and the game start from the same people.
    [Fact]
    public void The_starting_crew_is_the_benchmark_eight()
    {
        for (int n = 1; n <= 8; n++)
        {
            TeamMember expected = TestContent.Member(n);
            CrewDef actual = TestContent.Pack.Crew.Single(c => c.Id == expected.Id);

            Assert.Equal(expected.Seniority, actual.Seniority);
            Assert.Equal(expected.Skills.OrderBy(s => s.Key), actual.Skills.OrderBy(s => s.Key));
            Assert.Equal(expected.Caution, actual.Caution);
            Assert.Equal(expected.Discipline, actual.Discipline);
        }
    }

    [Fact]
    public void Crew_that_break_the_rules_are_reported_by_id()
    {
        Dictionary<string, string> files = Repo("crew/starting.yaml", """
            - { id: CREW_01, seniority: 0, skills: { SKILL_A: 1 }, caution: 0, discipline: 0 }
            - { id: CREW_02, seniority: 1, skills: { SKILL_Z: 1 }, caution: 0, discipline: 0 }
            - { id: CREW_03, seniority: 1, skills: { SKILL_A: 11 }, caution: 0, discipline: 0 }
            - { id: CREW_04, seniority: 1, skills: { SKILL_A: -1 }, caution: 3, discipline: 0 }
            - { id: CREW_05, seniority: 1, skills: { SKILL_A: 1 }, caution: 0, discipline: -3 }
            - { id: crew 6, seniority: 1, skills: { SKILL_A: 1 }, caution: 0, discipline: 0 }

            """);

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "crew/starting.yaml|Id 'crew 6' must be upper case letters, digits, and underscores, starting with a letter.",
                "crew/starting.yaml|CREW_01: seniority must be 1 or more.",
                "crew/starting.yaml|CREW_02: unknown skill 'SKILL_Z'.",
                "crew/starting.yaml|CREW_03: skill 'SKILL_A' must be 0 to 10, not 11.",
                "crew/starting.yaml|CREW_04: skill 'SKILL_A' must be 0 to 10, not -1.",
                "crew/starting.yaml|CREW_04: caution must be -2 to 2, not 3.",
                "crew/starting.yaml|CREW_05: discipline must be -2 to 2, not -3.",
            },
            Errors(result));
    }

    [Fact]
    public void A_pack_with_no_crew_is_an_error()
    {
        Dictionary<string, string> files = Repo();
        files.Remove("crew/starting.yaml");

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Equal(new[] { "crew/starting.yaml|The pack has no crew." }, Errors(result));
    }

    [Fact]
    public void Gear_that_breaks_the_rules_is_reported_by_id()
    {
        ContentLoadResult result = ContentLoader.Load(Repo("gear/base.yaml", "- { id: GEAR_01, tag: '' }\n- { id: GEAR_02 }\n- { id: gear 3, tag: tools }\n"));

        Assert.Equal(
            new[]
            {
                "gear/base.yaml|Id 'gear 3' must be upper case letters, digits, and underscores, starting with a letter.",
                "gear/base.yaml|GEAR_01: tag must not be empty.",
                "gear/base.yaml|GEAR_02: tag must not be empty.",
            },
            Errors(result));
    }

    [Fact]
    public void Assignments_that_break_the_rules_are_reported_by_id()
    {
        Dictionary<string, string> files = Repo("assignments/base.yaml", """
            - { id: ASSIGNMENT_TEMPLATE_1, objectives: { tags: [] }, offer: { 0: 1 }, loadoutLimit: 2 }
            - { id: ASSIGNMENT_TEMPLATE_2, objectives: { tags: [] }, offer: { 1: 0 }, loadoutLimit: 2 }
            - { id: ASSIGNMENT_TEMPLATE_3, objectives: { tags: [science] }, offer: { 2: 1 }, loadoutLimit: 2 }
            - { id: ASSIGNMENT_TEMPLATE_4, objectives: { tags: [] }, offer: { 1: 1 }, loadoutLimit: -1 }
            - { id: ASSIGNMENT_TEMPLATE_5, objectives: { tags: [] }, offer: { }, loadoutLimit: 0 }

            """);

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Equal(
            new[]
            {
                "assignments/base.yaml|ASSIGNMENT_TEMPLATE_1: offer count 0 must be 1 or more.",
                "assignments/base.yaml|ASSIGNMENT_TEMPLATE_2: offer weight for 1 must be 1 or more, not 0.",
                "assignments/base.yaml|ASSIGNMENT_TEMPLATE_3: offers up to 2 objectives, but only 1 objective templates match tags [science].",
                "assignments/base.yaml|ASSIGNMENT_TEMPLATE_4: loadoutLimit must be 0 or more.",
                "assignments/base.yaml|ASSIGNMENT_TEMPLATE_5: offer must list at least one count.",
            },
            Errors(result));
    }

    [Fact]
    public void A_pack_with_no_assignment_is_an_error()
    {
        Dictionary<string, string> files = Repo();
        files.Remove("assignments/base.yaml");

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Equal(new[] { "assignments/base.yaml|The pack has no assignment." }, Errors(result));
    }

    [Fact]
    public void An_unattended_difficulty_below_one_is_reported_by_objective()
    {
        string objectives = TestContent.RepoFiles()["objectives/base.yaml"].Replace("difficulty: 6", "difficulty: 0", StringComparison.Ordinal);

        ContentLoadResult result = ContentLoader.Load(Repo("objectives/base.yaml", objectives));

        Assert.Equal(new[] { "objectives/base.yaml|OBJECTIVE_TEMPLATE_1: ifUnattended.difficulty must be 1 or more." }, Errors(result));
    }

    [Theory]
    [InlineData("  maxLevel: 10", "  maxLevel: 0", "growth.maxLevel must be a whole number of 1 or more, not '0'.")]
    [InlineData("  xpBase: 10 ", "  xpBase: x ", "growth.xpBase must be a whole number of 1 or more, not 'x'.")]
    [InlineData("  lightWeeks: 2 ", "  lightWeeks: 0 ", "injury.lightWeeks must be a whole number of 1 or more, not '0'.")]
    [InlineData("  incapacitatedWeeks: 6", "  incapacitatedWeeks: 0", "injury.incapacitatedWeeks must be a whole number of 1 or more, not '0'.")]
    [InlineData("  perPoint: 2 ", "  perPoint: 2.5 ", "loyalty.perPoint must be a whole number, not '2.5'.")]
    [InlineData("  min: 0\n", "  min: 60\n", "loyalty.min must not be above loyalty.starting.")]
    [InlineData("  max: 100", "  max: 40", "loyalty.starting must not be above loyalty.max.")]
    [InlineData("  min: 0\n", "", "loyalty.min is missing.")]
    [InlineData("  max: 100", "  max: 100\n  extra: 1", "loyalty.extra is not a tuning value.")]
    public void The_crew_tuning_is_read_strictly(string from, string to, string message)
    {
        string tuning = TestContent.RepoFiles()["tuning/crew.yaml"];
        Assert.Contains(from, tuning, StringComparison.Ordinal);

        ContentLoadResult result = ContentLoader.Load(Repo("tuning/crew.yaml", tuning.Replace(from, to, StringComparison.Ordinal)));

        Assert.Null(result.Pack);
        Assert.Equal(new[] { $"tuning/crew.yaml|{message}" }, Errors(result));
    }

    [Fact]
    public void A_pack_without_the_crew_tuning_is_an_error()
    {
        Dictionary<string, string> files = Repo();
        files.Remove("tuning/crew.yaml");

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Equal(new[] { "tuning/crew.yaml|The pack has no tuning/crew.yaml." }, Errors(result));
    }

    private static Dictionary<string, string> Repo(string? replacePath = null, string? text = null)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        if (replacePath is not null)
        {
            files[replacePath] = text!;
        }

        return files;
    }

    private static string[] Errors(ContentLoadResult result)
    {
        return result.Errors.Select(e => $"{e.File}|{e.Message}").ToArray();
    }
}

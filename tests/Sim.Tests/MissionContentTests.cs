using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class MissionContentTests
{
    private const string Encounter = """
        - id: ENCOUNTER_01
          tags: [field]
          approaches:
            careful: { skill: SKILL_A, difficulty: 5, gearTag: scanner }
            fast: { skill: SKILL_A, difficulty: 6 }
          decision:
            id: DECISION_PROMPT_1
            stakes: 2
            bold: [{ momentum: 1 }, { strain: 1 }, { injuryRoll: light }]
            safe: [{ withdraw: true }]

        """;

    private const string Objective = """
        - id: OBJECTIVE_TEMPLATE_1
          teamSize: [2, 4]
          need: 1
          stages:
            - { slot: approach, draw: { tags: [field] }, count: 1 }

        """;

    [Fact]
    public void The_starter_pack_loads_from_disk()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Equal(9, pack.Encounters.Count);
        Assert.Equal(new[] { "OBJECTIVE_TEMPLATE_1", "OBJECTIVE_TEMPLATE_2" }, pack.Objectives.Select(o => o.Id));
        Assert.Equal(7, pack.Tuning.CheckTarget);
        Assert.Equal(3, pack.Bench.Mixed.Size);
    }

    [Fact]
    public void Encounters_and_objectives_load()
    {
        ContentPack pack = Load(Encounter, Objective).Pack!;

        EncounterDef encounter = Assert.Single(pack.Encounters);
        Assert.Equal(new ApproachDef("SKILL_A", 5, "scanner"), encounter.Careful);
        Assert.Equal(new EffectDef(Momentum: 1, Strain: 1, InjuryRoll: InjurySeverity.Light), encounter.Decision!.Bold);
        Assert.True(encounter.Decision.Safe.Withdraw);
        ObjectiveDef objective = Assert.Single(pack.Objectives);
        Assert.Equal((2, 4, 1), (objective.TeamSizeMin, objective.TeamSizeMax, objective.Need));
        Assert.Equal(new StageDef("approach", ["field"], 1, 1, false), objective.Stages[0], StageComparer.Instance);
    }

    [Fact]
    public void Broken_references_are_reported_together()
    {
        string encounter = Encounter
            .Replace("skill: SKILL_A, difficulty: 6", "skill: SKILL_Z, difficulty: 0")
            .Replace("stakes: 2", "stakes: 9")
            .Replace("{ strain: 1 }", "{ luck: 1 }")
            .Replace("tags: [field]", "tags: [field]\n  chain: ENCOUNTER_99");
        string objective = Objective.Replace("count: 1", "count: [1, 3]").Replace("need: 1", "need: 0");

        ContentLoadResult result = Load(encounter, objective);

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "encounters/a.yaml|ENCOUNTER_01: The fast approach uses unknown skill 'SKILL_Z'.",
                "encounters/a.yaml|ENCOUNTER_01: The fast approach's difficulty must be 1 or more.",
                "encounters/a.yaml|ENCOUNTER_01: Chains to unknown encounter 'ENCOUNTER_99'.",
                "encounters/a.yaml|ENCOUNTER_01: The decision's stakes must be 1 to 5.",
                "encounters/a.yaml|ENCOUNTER_01: Unknown effect 'luck: 1'.",
                "objectives/a.yaml|OBJECTIVE_TEMPLATE_1: need must be 1 or more.",
                "objectives/a.yaml|OBJECTIVE_TEMPLATE_1: Stage 'approach' draws up to 3 encounters tagged [field], but only 0 match.",
            },
            result.Errors.Select(e => $"{e.File}|{e.Message}"));
    }

    // A tuning number never falls back to a default: a typo must not quietly change the game.
    [Fact]
    public void A_missing_misspelt_or_malformed_tuning_value_is_an_error()
    {
        string tuning = TestContent.TuningText
            .Replace("  gearBonus: 2\n", "  gearBonis: 2\n")
            .Replace("  cautionWeight: 0.5\n", "  cautionWeight: lots\n");

        ContentLoadResult result = Load(Encounter, Objective, tuning);

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "check.gearBonus is missing.",
                "calls.cautionWeight must be a finite number, not 'lots'.",
                "check.gearBonis is not a tuning value.",
            },
            result.Errors.Select(e => e.Message));
    }

    [Theory]
    [InlineData("  collapseAt: 5\n", "  collapseAt: 6\n", "compounding.collapseAt")]
    [InlineData("  skipAtMomentum: 4\n", "  skipAtMomentum: 6\n", "compounding.skipAtMomentum")]
    [InlineData("  abortCallAt: 4\n", "  abortCallAt: 6\n", "compounding.abortCallAt")]
    public void A_compounding_threshold_above_the_cap_is_an_error(string from, string to, string name)
    {
        ContentLoadResult result = Load(Encounter, Objective, TestContent.TuningText.Replace(from, to));

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains(name, StringComparison.Ordinal) && e.Message.Contains("compounding.cap", StringComparison.Ordinal));
    }

    [Fact]
    public void An_abort_call_above_the_collapse_point_is_an_error()
    {
        ContentLoadResult result = Load(Encounter, Objective, TestContent.TuningText.Replace("  abortCallAt: 4\n", "  abortCallAt: 5\n").Replace("  collapseAt: 5\n", "  collapseAt: 4\n"));

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains("compounding.abortCallAt", StringComparison.Ordinal) && e.Message.Contains("compounding.collapseAt", StringComparison.Ordinal));
    }

    [Fact]
    public void Injury_thresholds_must_run_from_death_to_incapacity()
    {
        ContentLoadResult result = Load(Encounter, Objective, TestContent.TuningText.Replace("  deadBelow: 5 ", "  deadBelow: 9 "));

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains("injury.deadBelow", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("check:\n  dice: { count: 2, sides: 6 }", "check:\n  dice: { count: 2, sides: 1 }", "check.dice.sides must be a whole number of 2 or more, not '1'.")]
    [InlineData("injury:\n  dice: { count: 2, sides: 6 }", "injury:\n  dice: { count: 0, sides: 6 }", "injury.dice.count must be a whole number of 1 or more, not '0'.")]
    [InlineData("supervisorGapDivisor: 4", "supervisorGapDivisor: 0", "develop.supervisorGapDivisor must be a whole number of 1 or more, not '0'.")]
    public void Dice_and_divisors_that_cannot_work_are_errors_naming_the_value(string from, string to, string message)
    {
        ContentLoadResult result = Load(Encounter, Objective, TestContent.TuningText.Replace(from, to, StringComparison.Ordinal));

        Assert.Null(result.Pack);
        Assert.Equal(message, Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void The_close_margin_must_not_pass_the_near_margin()
    {
        ContentLoadResult result = Load(Encounter, Objective, TestContent.TuningText.Replace("  closeMargin: 3\n", "  closeMargin: 9\n"));

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains("experience.closeMargin", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("mixed:   { size: 3, skill: [2, 4]", "mixed:   { size: 3, skill: [5, 4]", "bench.teams.mixed.skill")]
    [InlineData("seniorSkill: [5, 6]", "seniorSkill: [7, 6]", "bench.teams.mixed.seniorSkill")]
    [InlineData("seniors: 1", "seniors: 4", "bench.teams.mixed.seniors")]
    public void A_bench_team_with_backwards_ranges_or_too_many_seniors_is_an_error(string from, string to, string name)
    {
        ContentLoadResult result = Load(Encounter, Objective, TestContent.TuningText.Replace(from, to));

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains(name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void A_number_that_is_not_finite_is_an_error(string text)
    {
        ContentLoadResult result = Load(Encounter, Objective, TestContent.TuningText.Replace("  base: 3\n", $"  base: {text}\n"));

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message.Contains("calls.base", StringComparison.Ordinal));
    }

    // The resolver never draws a chain target, so one cannot fill a stage.
    [Fact]
    public void A_chain_target_does_not_count_toward_a_stage()
    {
        string encounters = Encounter.Replace("tags: [field]", "tags: [field]\n  chain: ENCOUNTER_02") + Encounter.Replace("ENCOUNTER_01", "ENCOUNTER_02").Replace("DECISION_PROMPT_1", "DECISION_PROMPT_2");
        string objective = Objective.Replace("count: 1", "count: 2");

        ContentLoadResult result = Load(encounters, objective);

        Assert.Null(result.Pack);
        Assert.Equal("OBJECTIVE_TEMPLATE_1: Stage 'approach' draws up to 2 encounters tagged [field], but only 1 match.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void Stages_that_share_a_pool_must_all_fill_at_their_maximum_counts()
    {
        string encounters = Encounter + Encounter.Replace("ENCOUNTER_01", "ENCOUNTER_02").Replace("DECISION_PROMPT_1", "DECISION_PROMPT_2");
        string objective = Objective.Replace(
            "  stages:\n    - { slot: approach, draw: { tags: [field] }, count: 1 }",
            "  stages:\n    - { slot: approach, draw: { tags: [field] }, count: 1 }\n    - { slot: objective, draw: { tags: [field] }, count: [1, 2] }");

        ContentLoadResult result = Load(encounters, objective);

        Assert.Null(result.Pack);
        Assert.Equal(
            "OBJECTIVE_TEMPLATE_1: Stage 'objective' may find only 1 of the 2 encounters it draws, because earlier stages draw from the same pool.",
            Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void A_pack_without_a_tuning_file_is_an_error()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string> { ["pack.yaml"] = "id: CORE\nversion: 1\n" });

        Assert.Equal("The pack has no tuning/missions.yaml.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void A_tuning_file_loads_on_its_own_for_comparison()
    {
        (MissionTuning? tuning, _, IReadOnlyList<ContentError> errors) = ContentLoader.LoadTuningText(TestContent.TuningText.Replace("  gearBonus: 2\n", "  gearBonus: 3\n"));

        Assert.Empty(errors);
        Assert.Equal(TestContent.Tuning with { GearBonus = 3 }, tuning);
    }

    private static ContentLoadResult Load(string encounters, string objectives, string? tuning = null)
    {
        return ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = "id: CORE\nversion: 1\n",
            ["skills/a.yaml"] = "- id: SKILL_A\n",
            ["encounters/a.yaml"] = encounters,
            ["objectives/a.yaml"] = objectives,
            ["tuning/missions.yaml"] = tuning ?? TestContent.TuningText,
        });
    }

    // Records compare lists by reference, so stages are compared field by field.
    private sealed class StageComparer : IEqualityComparer<StageDef>
    {
        public static readonly StageComparer Instance = new();

        public bool Equals(StageDef? x, StageDef? y)
        {
            return x is not null && y is not null && x.Slot == y.Slot && x.DrawTags.SequenceEqual(y.DrawTags)
                && x.CountMin == y.CountMin && x.CountMax == y.CountMax && x.Skippable == y.Skippable;
        }

        public int GetHashCode(StageDef obj)
        {
            return obj.Slot.GetHashCode(StringComparison.Ordinal);
        }
    }
}

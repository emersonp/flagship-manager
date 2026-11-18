using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class MissionContentTests
{
    private const string Encounter = """
        - id: ENCOUNTER_01
          tags: [field]
          approaches:
            - { id: careful, skill: SKILL_A, difficulty: 5, gearTag: scanner }
            - { id: fast, skill: SKILL_A, difficulty: 6 }
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
        ApproachDef careful = encounter.Approaches[0];
        Assert.Equal(("careful", "SKILL_A", 5, "scanner", (string?)null), (careful.Id, careful.Skill, careful.Difficulty, careful.GearTag, careful.Name));
        Assert.Empty(careful.Requires);
        Assert.Empty(careful.Outcomes);
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

    [Fact]
    public void An_encounter_with_no_approaches_is_refused()
    {
        ContentLoadResult result = Load(Encounter.Replace("    - { id: careful, skill: SKILL_A, difficulty: 5, gearTag: scanner }\n    - { id: fast, skill: SKILL_A, difficulty: 6 }\n", ""), Objective);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message == "ENCOUNTER_01: An encounter needs at least one approach.");
    }

    [Fact]
    public void An_encounter_with_five_approaches_is_refused()
    {
        string more = string.Concat(new[] { "a", "b", "c" }.Select(id => $"    - {{ id: {id}, skill: SKILL_A, difficulty: 6 }}\n"));

        ContentLoadResult result = Load(Encounter.Replace("  decision:", more + "  decision:"), Objective);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message == "ENCOUNTER_01: An encounter may have at most 4 approaches.");
    }

    [Fact]
    public void Two_approaches_with_one_id_are_refused()
    {
        ContentLoadResult result = Load(Encounter.Replace("id: fast", "id: careful"), Objective);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message == "ENCOUNTER_01: Approach id 'careful' is used twice.");
    }

    [Theory]
    [InlineData("Fast")]
    [InlineData("go fast")]
    [InlineData("fast-track")]
    [InlineData("a23456789012345678901234567890123456789012")]
    public void An_approach_id_outside_the_inner_rule_is_refused(string id)
    {
        ContentLoadResult result = Load(Encounter.Replace("id: fast", $"id: {id}"), Objective);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message == $"ENCOUNTER_01: Approach id '{id}' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.");
    }

    [Fact]
    public void A_tag_with_a_hyphen_is_refused()
    {
        ContentLoadResult result = Load(Encounter.Replace("tags: [field]", "tags: [field-work]"), Objective);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message == "ENCOUNTER_01: Tag 'field-work' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.");
    }

    [Fact]
    public void Approaches_load_in_the_order_written()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Equal(["careful", "fast"], pack.Encounters.Single(e => e.Id == "ENCOUNTER_01").Approaches.Select(a => a.Id));
        Assert.Equal(["APPROACH_A_NAME", "APPROACH_B_NAME"], pack.Encounters.Single(e => e.Id == "ENCOUNTER_01").Approaches.Select(a => a.Name));
        Assert.Equal(["fast", "careful"], Load(Encounter.Replace("id: careful", "id: first").Replace("id: fast", "id: careful").Replace("id: first", "id: fast"), Objective).Pack!.Encounters[0].Approaches.Select(a => a.Id));
    }

    [Fact]
    public void An_approach_name_follows_the_name_rule()
    {
        ContentLoadResult result = Load(Encounter.Replace("id: fast,", $"id: fast, name: \"{new string('x', 81)}\","), Objective);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message == "ENCOUNTER_01 approach fast: name must be 1 to 80 characters with no line break.");
    }

    // --- Requirements and outcomes (approaches plan, Task 4) ---

    private static string FastWith(string keys)
    {
        return Encounter.Replace("{ id: fast, skill: SKILL_A, difficulty: 6 }", $"{{ id: fast, skill: SKILL_A, difficulty: 6, {keys} }}");
    }

    private static void AssertRefused(string encounter, string message)
    {
        ContentLoadResult result = Load(encounter, Objective);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.Message == $"ENCOUNTER_01: {message}");
    }

    [Fact]
    public void Requirements_and_outcomes_load()
    {
        ContentPack pack = Load(FastWith("requires: [{ skill: SKILL_A, level: 3 }, { gear: scanner }], outcomes: { success: [{ progress: 1 }, { momentum: 2 }], failure: [{ strain: 1 }, { injuryRoll: severe }] }"), Objective).Pack!;

        ApproachDef fast = pack.Encounters[0].Approaches[1];
        Assert.Equal([new RequirementDef("SKILL_A", 3, null), new RequirementDef(null, 0, "scanner")], fast.Requires);
        Assert.Equal(new EffectDef(Momentum: 2, Progress: 1), fast.Outcomes["success"]);
        Assert.Equal(new EffectDef(Strain: 1, InjuryRoll: InjurySeverity.Severe), fast.Outcomes["failure"]);
        Assert.Equal(["failure", "success"], fast.Outcomes.Keys.Order(StringComparer.Ordinal));
    }

    // The data has no limit on requirements; only the form edits a fixed number.
    [Fact]
    public void An_approach_may_have_five_requirements()
    {
        string five = string.Join(", ", Enumerable.Range(1, 4).Select(n => $"{{ skill: SKILL_A, level: {n} }}").Append("{ gear: scanner }"));

        ContentLoadResult result = Load(FastWith($"requires: [{five}]"), Objective);

        Assert.Empty(result.Errors);
        Assert.Equal(5, result.Pack!.Encounters[0].Approaches[1].Requires.Count);
    }

    [Fact]
    public void An_empty_outcome_loads_as_no_effect_not_as_absent()
    {
        ApproachDef fast = Load(FastWith("outcomes: { partial: [] }"), Objective).Pack!.Encounters[0].Approaches[1];

        Assert.True(fast.Outcomes.ContainsKey("partial"));
        Assert.Equal(new EffectDef(), fast.Outcomes["partial"]);
    }

    [Fact]
    public void A_requirement_naming_both_a_skill_and_gear_is_refused()
    {
        AssertRefused(FastWith("requires: [{ skill: SKILL_A, level: 1, gear: scanner }]"), "The fast approach's requirement 1 must name one of skill, gear, and trait.");
    }

    [Fact]
    public void A_requirement_naming_neither_a_skill_nor_gear_is_refused()
    {
        AssertRefused(FastWith("requires: [{ gear: scanner }, { level: 2 }]"), "The fast approach's requirement 2 must name one of skill, gear, and trait.");
    }

    [Theory]
    [InlineData("{ skill: SKILL_A }")]
    [InlineData("{ skill: SKILL_A, level: 0 }")]
    public void A_skill_requirement_below_level_1_is_refused(string requirement)
    {
        AssertRefused(FastWith($"requires: [{requirement}]"), "The fast approach's requirement 1 needs a level of 1 or more.");
    }

    [Fact]
    public void A_skill_requirement_on_an_unknown_skill_is_refused()
    {
        AssertRefused(FastWith("requires: [{ skill: SKILL_Z, level: 2 }]"), "The fast approach requires unknown skill 'SKILL_Z'.");
    }

    [Fact]
    public void A_requirement_on_a_gear_tag_no_gear_carries_is_refused()
    {
        AssertRefused(FastWith("requires: [{ gear: cloak }]"), "Requires gear tagged 'cloak', which no gear carries.");
    }

    [Fact]
    public void An_outcome_under_a_word_that_is_not_a_tier_is_refused()
    {
        AssertRefused(FastWith("outcomes: { triumph: [{ progress: 2 }] }"), "The fast approach has an outcome for 'triumph', which is not a tier: critical, success, partial, failure, or disaster.");
    }

    [Fact]
    public void An_outcome_that_withdraws_is_refused()
    {
        AssertRefused(FastWith("outcomes: { disaster: [{ withdraw: true }] }"), "An outcome cannot withdraw the team.");
    }

    // --- Traits (flags in 1e part one, Task 1) ---

    private static string WithTraits(string outcome, string bold)
    {
        return FastWith($"outcomes: {{ partial: {outcome} }}").Replace("bold: [{ momentum: 1 }, { strain: 1 }, { injuryRoll: light }]", $"bold: {bold}");
    }

    [Fact]
    public void A_trait_change_loads_on_an_outcome_and_on_a_decision()
    {
        ContentLoadResult result = Load(WithTraits("[{ strain: 1 }, { trait: trait_a, add: 1, on: ship }, { trait: sensors_quirk, add: -2, on: actor }]", "[{ progress: 1 }, { trait: trait_b, set: 0, on: lead }, { trait: trait_c, set: 2, on: team }]"), Objective);

        Assert.Empty(result.Errors);
        EncounterDef encounter = result.Pack!.Encounters[0];
        EffectDef partial = encounter.Approaches[1].Outcomes["partial"];
        Assert.Equal(1, partial.Strain);
        Assert.Equal([new TraitChange("trait_a", 1, false, "ship"), new TraitChange("sensors_quirk", -2, false, "actor")], partial.Traits!);
        Assert.Equal(1, encounter.Decision!.Bold.Progress);
        Assert.Equal([new TraitChange("trait_b", 0, true, "lead"), new TraitChange("trait_c", 2, true, "team")], encounter.Decision.Bold.Traits!);
        Assert.Null(encounter.Decision.Safe.Traits);
        Assert.Null(encounter.Approaches[0].Outcomes.GetValueOrDefault("partial")?.Traits);
    }

    [Theory]
    [InlineData("{ trait: trait_a, add: 1, set: 1, on: ship }")]
    [InlineData("{ trait: trait_a, on: ship }")]
    public void A_trait_change_with_both_or_neither_of_add_and_set_is_refused(string change)
    {
        AssertRefused(WithTraits($"[{change}]", "[]"), "A trait change needs add or set, not both.");
    }

    [Theory]
    [InlineData("{ trait: trait_a, add: 1 }")]
    [InlineData("{ trait: trait_a, add: 1, on: crew }")]
    public void A_trait_change_with_no_or_an_unknown_on_is_refused(string change)
    {
        AssertRefused(WithTraits($"[{change}]", "[]"), "A trait change needs on: ship, actor, lead, or team.");
    }

    [Fact]
    public void A_trait_change_on_the_actor_in_a_decision_is_refused()
    {
        AssertRefused(WithTraits("[]", "[{ trait: trait_a, add: 1, on: actor }]"), "on: actor is only for an approach's outcome.");
    }

    [Theory]
    [InlineData("TRAIT_A")]
    [InlineData("flag a")]
    [InlineData("flag-a")]
    [InlineData("1st")]
    [InlineData("a23456789012345678901234567890123456789012")]
    public void A_trait_id_outside_the_rule_is_refused(string id)
    {
        AssertRefused(WithTraits($"[{{ trait: \"{id}\", add: 1, on: ship }}]", "[]"), $"Trait id '{id}' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.");
    }

    [Fact]
    public void A_trait_set_below_zero_is_refused()
    {
        AssertRefused(WithTraits("[{ trait: trait_a, set: -1, on: ship }]", "[]"), "A trait's set must be 0 or more.");
    }

    [Fact]
    public void An_encounter_whose_every_approach_has_requirements_is_refused()
    {
        string encounter = FastWith("requires: [{ gear: tools }]").Replace("difficulty: 5, gearTag: scanner }", "difficulty: 5, gearTag: scanner, requires: [{ gear: scanner }] }");

        AssertRefused(encounter, "At least one approach must have no requirements.");
    }

    // Decision ids key the game's calls and its display names, so each is used once in the whole pack.
    [Fact]
    public void A_decision_id_used_by_two_encounters_is_an_error()
    {
        string second = Encounter.Replace("ENCOUNTER_01", "ENCOUNTER_02");

        ContentLoadResult result = Load(Encounter + second, Objective);

        Assert.Null(result.Pack);
        ContentError error = Assert.Single(result.Errors);
        Assert.Equal(("encounters/a.yaml", "ENCOUNTER_02: decision id 'DECISION_PROMPT_1' is also used by ENCOUNTER_01."), (error.File, error.Message));
    }

    // The resolver raises the abort call with this id.
    [Fact]
    public void The_abort_decision_id_is_reserved()
    {
        ContentLoadResult result = Load(Encounter.Replace("DECISION_PROMPT_1", "DECISION_ABORT"), Objective);

        Assert.Null(result.Pack);
        ContentError error = result.Errors[0];
        Assert.Equal(("encounters/a.yaml", "ENCOUNTER_01: decision id 'DECISION_ABORT' is reserved for the abort call."), (error.File, error.Message));
    }

    [Fact]
    public void An_encounter_that_chains_to_itself_is_an_error()
    {
        ContentLoadResult result = Load(Encounter.Replace("tags: [field]", "tags: [field]\n  chain: ENCOUNTER_01"), Objective);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e is { File: "encounters/a.yaml", Message: "ENCOUNTER_01: chains to itself." });
    }

    // A stage written without draw, with an empty draw, or with empty tags draws by no tag.
    [Theory]
    [InlineData("{ slot: approach, count: 1 }")]
    [InlineData("{ slot: approach, draw: {}, count: 1 }")]
    [InlineData("{ slot: approach, draw: ~, count: 1 }")]
    [InlineData("{ slot: approach, draw: { tags: [] }, count: 1 }")]
    public void A_stage_with_no_draw_tags_reads_the_same_however_it_is_written(string stage)
    {
        ContentLoadResult result = Load(Encounter, Objective.Replace("{ slot: approach, draw: { tags: [field] }, count: 1 }", stage));

        Assert.Empty(result.Errors);
        Assert.Empty(result.Pack!.Objectives[0].Stages[0].DrawTags);
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
        ContentLoadResult result = ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string> { ["pack.yaml"] = "id: CORE\nversion: 1\n" }));

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
        return ContentLoader.Load(TestContent.WithCrewFiles(new Dictionary<string, string>
        {
            ["pack.yaml"] = "id: CORE\nversion: 1\n",
            ["skills/a.yaml"] = "- id: SKILL_A\n",
            ["encounters/a.yaml"] = encounters,
            ["objectives/a.yaml"] = objectives,
            ["tuning/missions.yaml"] = tuning ?? TestContent.TuningText,
        }));
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

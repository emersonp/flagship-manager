using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class ContentWriterTests : IClassFixture<RepoContentGuard>
{
    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    private static string Render(params IContentModel[] items) => ContentWriter.Render(null, items);

    private static ContentFileSet Repo() => ContentFileSet.ReadDirectory(Path.Combine(TestContent.Root, "tests", "fixtures", "content"));

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<EffectModel>> NoOutcomes = new Dictionary<string, IReadOnlyList<EffectModel>>();

    [Fact]
    public void A_skill_is_written_with_its_name_and_tags()
    {
        Assert.Equal(
            Lines("- id: SKILL_A", "  name: NAME_PLACEHOLDER", "  tags: [technical, social]", "- id: SKILL_B"),
            Render(new SkillModel("SKILL_A", "NAME_PLACEHOLDER", ["technical", "social"]), new SkillModel("SKILL_B", null, [])));
    }

    [Fact]
    public void Gear_is_written_as_a_block_item()
    {
        Assert.Equal(Lines("- id: GEAR_01", "  name: NAME_PLACEHOLDER", "  tag: scanner"), Render(new GearModel("GEAR_01", "NAME_PLACEHOLDER", "scanner")));
    }

    [Fact]
    public void Crew_is_written_with_skills_as_a_flow_map_in_the_order_given()
    {
        Assert.Equal(
            Lines("- id: CREW_01", "  name: NAME_PLACEHOLDER", "  seniority: 5", "  skills: { SKILL_B: 6, SKILL_A: 5 }", "  caution: -2", "  discipline: 2", "- id: CREW_02", "  seniority: 1", "  skills: {}", "  caution: 0", "  discipline: 0"),
            Render(
                new CrewModel("CREW_01", "NAME_PLACEHOLDER", 5, new Dictionary<string, int> { ["SKILL_B"] = 6, ["SKILL_A"] = 5 }, -2, 2),
                new CrewModel("CREW_02", null, 1, new Dictionary<string, int>(), 0, 0)));
    }

    [Fact]
    public void An_assignment_is_written_with_its_offer_as_a_flow_map()
    {
        Assert.Equal(
            Lines("- id: ASSIGNMENT_1", "  name: NAME_PLACEHOLDER", "  objectives: { tags: [science, repair] }", "  offer: { 1: 6, 2: 3 }", "  loadoutLimit: 2"),
            Render(new AssignmentModel("ASSIGNMENT_1", "NAME_PLACEHOLDER", ["science", "repair"], new Dictionary<int, int> { [1] = 6, [2] = 3 }, 2)));
        Assert.Equal(
            Lines("- id: ASSIGNMENT_1", "  objectives: { tags: [] }", "  offer: { 1: 1 }", "  loadoutLimit: 0"),
            Render(new AssignmentModel("ASSIGNMENT_1", null, [], new Dictionary<int, int> { [1] = 1 }, 0)));
    }

    // A stage with no draw tags has no draw key: the canonical form, which the loader reads as no tags.
    [Fact]
    public void A_stage_with_no_draw_tags_is_written_without_a_draw_key()
    {
        Assert.Equal(
            Lines("- id: OBJECTIVE_1", "  tags: []", "  teamSize: 1", "  need: 1", "  stages:", "    - { slot: approach, count: 1 }"),
            Render(new ObjectiveModel("OBJECTIVE_1", null, [], 1, 1, 1, null, [new StageModel("approach", [], 1, 1, false)])));
    }

    [Fact]
    public void An_objective_is_written_with_a_pair_or_a_number_for_each_count()
    {
        Assert.Equal(
            Lines(
                "- id: OBJECTIVE_1",
                "  name: NAME_PLACEHOLDER",
                "  tags: [science]",
                "  teamSize: [2, 4]",
                "  need: 4",
                "  ifUnattended: { difficulty: 6 }",
                "  stages:",
                "    - { slot: approach, draw: { tags: [field] }, count: 1 }",
                "    - { slot: objective, draw: { tags: [hazard, field] }, count: [1, 2], skippable: true }",
                "- id: OBJECTIVE_2",
                "  tags: []",
                "  teamSize: 3",
                "  need: 1",
                "  stages: []"),
            Render(
                new ObjectiveModel("OBJECTIVE_1", "NAME_PLACEHOLDER", ["science"], 2, 4, 4, 6, [new StageModel("approach", ["field"], 1, 1, false), new StageModel("objective", ["hazard", "field"], 1, 2, true)]),
                new ObjectiveModel("OBJECTIVE_2", null, [], 3, 3, 1, null, [])));
    }

    [Fact]
    public void An_encounter_is_written_with_its_approaches_and_decision()
    {
        Assert.Equal(
            Lines(
                "- id: ENCOUNTER_01",
                "  name: NAME_PLACEHOLDER",
                "  tags: [field, hazard]",
                "  commsBlocked: true",
                "  chain: ENCOUNTER_CHAIN",
                "  approaches:",
                "    - { id: careful, skill: SKILL_A, difficulty: 5, gearTag: scanner }",
                "    - { id: fast, skill: SKILL_C, difficulty: 6 }",
                "  decision:",
                "    id: DECISION_PROMPT_1",
                "    name: NAME_PLACEHOLDER",
                "    stakes: 2",
                "    bold: [{ progress: 1 }, { momentum: -1, strain: 2 }, { injuryRoll: light }, { withdraw: true }]",
                "    safe: []",
                "- id: ENCOUNTER_CHAIN",
                "  tags: []",
                "  approaches:",
                "    - { id: careful, skill: SKILL_C, difficulty: 6 }",
                "    - { id: fast, skill: SKILL_A, difficulty: 7 }"),
            Render(
                new EncounterModel(
                    "ENCOUNTER_01",
                    "NAME_PLACEHOLDER",
                    ["field", "hazard"],
                    true,
                    "ENCOUNTER_CHAIN",
                    [new ApproachModel("careful", null, "SKILL_A", 5, "scanner", [], NoOutcomes), new ApproachModel("fast", null, "SKILL_C", 6, null, [], NoOutcomes)],
                    new DecisionModel("DECISION_PROMPT_1", "NAME_PLACEHOLDER", 2, [new EffectModel(1, null, null, null), new EffectModel(null, -1, 2, null), new EffectModel(null, null, null, "light"), new EffectModel(null, null, null, null, true)], [])),
                new EncounterModel("ENCOUNTER_CHAIN", null, [], false, null, [new ApproachModel("careful", null, "SKILL_C", 6, null, [], NoOutcomes), new ApproachModel("fast", null, "SKILL_A", 7, null, [], NoOutcomes)], null)));
    }

    // An approach with words is a block, one key to a line; one without stays on one line. The order written is kept.
    [Fact]
    public void An_encounter_with_three_approaches_one_with_words_writes_and_reads_back_equal()
    {
        var model = new EncounterModel(
            "ENCOUNTER_01",
            null,
            ["field"],
            false,
            null,
            [
                new ApproachModel("careful", "APPROACH_A_NAME", "SKILL_A", 5, "scanner", [], NoOutcomes),
                new ApproachModel("fast", "APPROACH_B_NAME", "SKILL_C", 6, null, [], NoOutcomes, new Dictionary<string, string> { ["attempt"] = "actor: Now.", ["failure"] = "#actor# slipped." }),
                new ApproachModel("third-way", null, "SKILL_B", 7, null, [], NoOutcomes),
            ],
            null);

        string text = Render(model);

        Assert.Equal(
            Lines(
                "- id: ENCOUNTER_01",
                "  tags: [field]",
                "  approaches:",
                "    - { id: careful, name: APPROACH_A_NAME, skill: SKILL_A, difficulty: 5, gearTag: scanner }",
                "    - id: fast",
                "      name: APPROACH_B_NAME",
                "      skill: SKILL_C",
                "      difficulty: 6",
                "      text:",
                "        attempt: \"actor: Now.\"",
                "        failure: \"#actor# slipped.\"",
                "    - { id: third-way, skill: SKILL_B, difficulty: 7 }"),
            text);
        ContentFileSet files = ContentEdits.ReplaceText(Repo(), "encounters/base.yaml", text);
        EncounterModel read = ContentEdits.Get<EncounterModel>(files, ContentKind.Encounter, "ENCOUNTER_01")!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(model), System.Text.Json.JsonSerializer.Serialize(read));
    }

    // Requirements and outcomes, an empty outcome among them, write and read back equal: on one line, and in a block beside words.
    [Fact]
    public void Requirements_and_outcomes_write_and_read_back_equal()
    {
        RequirementModel[] five = [new("SKILL_A", 4, null), new("SKILL_B", 2, null), new("SKILL_C", 1, null), new("SKILL_D", 3, null), new(null, null, "scanner")];
        var outcomes = new Dictionary<string, IReadOnlyList<EffectModel>>
        {
            ["critical"] = [new EffectModel(1, null, null, null), new EffectModel(null, null, -1, null), new EffectModel(null, 2, null, null)],
            ["partial"] = [],
            ["disaster"] = [new EffectModel(null, null, 2, "light")],
        };
        var model = new EncounterModel(
            "ENCOUNTER_01",
            null,
            ["field"],
            false,
            null,
            [
                new ApproachModel("careful", null, "SKILL_A", 5, null, [], new Dictionary<string, IReadOnlyList<EffectModel>>()),
                new ApproachModel("fast", null, "SKILL_C", 6, null, five, outcomes),
                new ApproachModel("third-way", null, "SKILL_B", 7, "tools", [new RequirementModel(null, null, "tools")], new Dictionary<string, IReadOnlyList<EffectModel>> { ["success"] = [new EffectModel(null, 1, null, null)] }, new Dictionary<string, string> { ["attempt"] = "actor: Now." }),
            ],
            null);

        string text = Render(model);

        Assert.Equal(
            Lines(
                "- id: ENCOUNTER_01",
                "  tags: [field]",
                "  approaches:",
                "    - { id: careful, skill: SKILL_A, difficulty: 5 }",
                "    - { id: fast, skill: SKILL_C, difficulty: 6, requires: [{ skill: SKILL_A, level: 4 }, { skill: SKILL_B, level: 2 }, { skill: SKILL_C, level: 1 }, { skill: SKILL_D, level: 3 }, { gear: scanner }], outcomes: { critical: [{ progress: 1 }, { strain: -1 }, { momentum: 2 }], partial: [], disaster: [{ strain: 2, injuryRoll: light }] } }",
                "    - id: third-way",
                "      skill: SKILL_B",
                "      difficulty: 7",
                "      gearTag: tools",
                "      requires: [{ gear: tools }]",
                "      outcomes:",
                "        success: [{ momentum: 1 }]",
                "      text:",
                "        attempt: \"actor: Now.\""),
            text);
        ContentFileSet files = ContentEdits.ReplaceText(Repo(), "encounters/base.yaml", text);
        EncounterModel read = ContentEdits.Get<EncounterModel>(files, ContentKind.Encounter, "ENCOUNTER_01")!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(model), System.Text.Json.JsonSerializer.Serialize(read));
    }

    // Add, set, each target, and two changes in one outcome write in the order trait, add or set, on, and read back equal.
    [Fact]
    public void Trait_changes_write_and_read_back_equal()
    {
        var outcomes = new Dictionary<string, IReadOnlyList<EffectModel>>
        {
            ["partial"] = [new EffectModel(null, null, 1, null), new EffectModel(null, null, null, null, false, "trait_a", 1, false, "ship")],
            ["disaster"] = [new EffectModel(null, null, null, null, false, "sensors_quirk", -2, false, "actor"), new EffectModel(null, null, null, null, false, "trait_b", 0, true, "team")],
        };
        var model = new EncounterModel(
            "ENCOUNTER_01",
            null,
            ["field"],
            false,
            null,
            [new ApproachModel("careful", null, "SKILL_A", 5, null, [], outcomes)],
            new DecisionModel("DECISION_PROMPT_1", null, 2, [new EffectModel(1, null, null, null, false, "trait_c", 2, true, "lead")], []));

        string text = Render(model);

        Assert.Equal(
            Lines(
                "- id: ENCOUNTER_01",
                "  tags: [field]",
                "  approaches:",
                "    - { id: careful, skill: SKILL_A, difficulty: 5, outcomes: { partial: [{ strain: 1 }, { trait: trait_a, add: 1, on: ship }], disaster: [{ trait: sensors_quirk, add: -2, on: actor }, { trait: trait_b, set: 0, on: team }] } }",
                "  decision:",
                "    id: DECISION_PROMPT_1",
                "    stakes: 2",
                "    bold: [{ progress: 1, trait: trait_c, set: 2, on: lead }]",
                "    safe: []"),
            text);
        ContentFileSet files = ContentEdits.ReplaceText(Repo(), "encounters/base.yaml", text);
        EncounterModel read = ContentEdits.Get<EncounterModel>(files, ContentKind.Encounter, "ENCOUNTER_01")!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(model), System.Text.Json.JsonSerializer.Serialize(read));
    }

    // 1e part six: a storylet as the files write one: notes after the name, expressions double-quoted, the script as a block, options as
    // blocks, a check's success and failure under it, effects as flow maps. It reads back equal.
    [Fact]
    public void A_storylet_is_written_in_the_house_style_and_reads_back_equal()
    {
        var model = new StoryletModel(
            "STORYLET_01",
            "A storylet",
            3,
            "ship.trait.trait_a == 1",
            [new CastSlotModel("subject", "subject.aboard"), new CastSlotModel("target", "subject.trait.trait_g.toward(target) >= 1")],
            [
                new StoryletOptionModel("talk", "Talk", null, [], new StoryletCheckModel("SKILL_A", 4, new StoryletOutcomeModel([new StoryletEffectModel("trait", null, "trait_e", true, "level_a", "subject")], "#handler# got through."), new StoryletOutcomeModel([new StoryletEffectModel("morale", -1, On: "handler")], null)), null),
                new StoryletOptionModel("leave", "Leave it", "subject.loyalty >= 60", [new StoryletEffectModel("trait", 2, "trait_g", false, null, "subject", "target"), new StoryletEffectModel("loyalty", -1, On: "crew")], null, "Left alone.", new ChronicleModel(4, "#subject# was left.")),
                new StoryletOptionModel("nothing", "Nothing", null, [], null, null),
            ],
            8,
            2,
            "subject: One.\n= #subject# two.",
            new ChronicleModel(3, null),
            "Why: the first line.\n\n# Not a comment.");

        string text = Render(model);

        Assert.Equal(
            Lines(
                "- id: STORYLET_01",
                "  name: A storylet",
                "  notes: |-",
                "    Why: the first line.",
                "",
                "    # Not a comment.",
                "  stakes: 3",
                "  requires: \"ship.trait.trait_a == 1\"",
                "  cast:",
                "    subject: \"subject.aboard\"",
                "    target: \"subject.trait.trait_g.toward(target) >= 1\"",
                "  cooldown: { cast: 8, global: 2 }",
                "  chronicle: { weight: 3 }",
                "  text: |-",
                "    subject: One.",
                "    = #subject# two.",
                "  options:",
                "    - id: talk",
                "      label: Talk",
                "      check: { skill: SKILL_A, difficulty: 4 }",
                "      success:",
                "        effects: [{ trait: trait_e, set: level_a, on: subject }]",
                "        text: \"#handler# got through.\"",
                "      failure:",
                "        effects: [{ morale: -1, on: handler }]",
                "    - id: leave",
                "      label: Leave it",
                "      requires: \"subject.loyalty >= 60\"",
                "      effects: [{ trait: trait_g, add: 2, on: subject, toward: target }, { loyalty: -1, on: crew }]",
                "      chronicle: { weight: 4, text: \"#subject# was left.\" }",
                "      text: Left alone.",
                "    - id: nothing",
                "      label: Nothing",
                "      effects: []"),
            text);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(model), System.Text.Json.JsonSerializer.Serialize(ReadBack<StoryletModel>(ContentKind.Storylet, "storylets/base.yaml", text, "STORYLET_01")));
    }

    // A vignette with no choice writes its effects last; one with a choice writes its default before its options.
    [Fact]
    public void A_vignette_is_written_in_the_house_style_and_reads_back_equal()
    {
        var plain = new VignetteModel("VIGNETTE_01", null, null, [new CastSlotModel("subject", "subject.history.last('rested')")], [new StoryletEffectModel("morale", 2, On: "subject")], [], null, null, null, "subject: Rested.", "One line.");
        var choice = new VignetteModel("VIGNETTE_02", null, "ship.trait.trait_a == 2", [], [], [new StoryletOptionModel("option_a", "Yes", null, [new StoryletEffectModel("loyalty", 1, On: "crew")], null, "Done."), new StoryletOptionModel("option_b", "No", null, [], null, null)], "option_b", 3, null, null);

        string text = Render(plain, choice);

        Assert.Equal(
            Lines(
                "- id: VIGNETTE_01",
                "  notes: |-",
                "    One line.",
                "  cast:",
                "    subject: \"subject.history.last('rested')\"",
                "  text: \"subject: Rested.\"",
                "  effects: [{ morale: 2, on: subject }]",
                "- id: VIGNETTE_02",
                "  requires: \"ship.trait.trait_a == 2\"",
                "  cooldown: { cast: 3 }",
                "  default: option_b",
                "  options:",
                "    - id: option_a",
                "      label: \"Yes\"",
                "      effects: [{ loyalty: 1, on: crew }]",
                "      text: Done.",
                "    - id: option_b",
                "      label: \"No\"",
                "      effects: []"),
            text);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(plain), System.Text.Json.JsonSerializer.Serialize(ReadBack<VignetteModel>(ContentKind.Vignette, "vignettes/base.yaml", text, "VIGNETTE_01")));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(choice), System.Text.Json.JsonSerializer.Serialize(ReadBack<VignetteModel>(ContentKind.Vignette, "vignettes/base.yaml", text, "VIGNETTE_02")));
    }

    // A trait in the key order of traits/base.yaml; a rule's change on the trait itself leaves the trait out.
    [Fact]
    public void A_trait_is_written_in_the_house_style_and_reads_back_equal()
    {
        var model = new TraitModel(
            "trait_g",
            "Avoids",
            false,
            "pair",
            new Dictionary<int, string> { [1] = "level_a", [2] = "level_b" },
            5,
            "Avoids",
            new TraitLaunchModel([new StoryletEffectModel("trait", null, null, true, "level_b"), new StoryletEffectModel("loyalty", -2)], [], [new StoryletEffectModel("morale", -3)]),
            new TraitMissionModel(-1, true),
            new TraitStepModel(4, -1),
            6,
            [new StoryletEffectModel("trait", 0, null, true), new StoryletEffectModel("trait", 1, "trait_j", false)],
            "A pair.");

        string text = Render(model);

        Assert.Equal(
            Lines(
                "- id: trait_g",
                "  name: Avoids",
                "  notes: |-",
                "    A pair.",
                "  shown: false",
                "  on: pair",
                "  levels: { 1: level_a, 2: level_b }",
                "  max: 5",
                "  briefing: Avoids",
                "  launch:",
                "    sent: [{ set: level_b }, { loyalty: -2 }]",
                "    withTarget: [{ morale: -3 }]",
                "  mission: { checkBonus: -1, withTarget: true }",
                "  every: { weeks: 4, add: -1 }",
                "  expires: { weeks: 6 }",
                "  onTargetDied: [{ set: 0 }, { trait: trait_j, add: 1 }]"),
            text);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(model), System.Text.Json.JsonSerializer.Serialize(ReadBack<TraitModel>(ContentKind.Trait, "traits/base.yaml", text, "trait_g")));
    }

    // A note the block cannot hold exactly (a space at the end of a line) is quoted on one line instead, and reads back the same.
    [Fact]
    public void A_note_a_block_cannot_hold_is_quoted_and_reads_back_the_same()
    {
        var model = new TraitModel("trait_a", null, null, null, null, null, null, null, null, null, null, [], "Ends in a space: \nthen # this");

        string text = Render(model);

        Assert.Equal(Lines("- id: trait_a", "  notes: \"Ends in a space: \\nthen # this\""), text);
        Assert.Equal("Ends in a space: \nthen # this", ReadBack<TraitModel>(ContentKind.Trait, "traits/base.yaml", text, "trait_a").Notes);
    }

    private static T ReadBack<T>(ContentKind kind, string path, string text, string id)
        where T : class
    {
        return ContentEdits.Get<T>(ContentEdits.ReplaceText(Repo(), path, text), kind, id)!;
    }

    // Slice 1e part five: a decision's chronicle weight is written after its labels, and read back the same; 0 is kept.
    [Fact]
    public void A_decisions_chronicle_weight_is_written_and_read_back()
    {
        var model = new EncounterModel(
            "ENCOUNTER_01",
            null,
            ["field"],
            false,
            null,
            [new ApproachModel("careful", null, "SKILL_A", 5, null, [], new Dictionary<string, IReadOnlyList<EffectModel>>())],
            new DecisionModel("DECISION_PROMPT_1", null, 2, [new EffectModel(1, null, null, null)], [], ChronicleWeight: 0));

        string text = Render(model);

        Assert.Equal(
            Lines(
                "- id: ENCOUNTER_01",
                "  tags: [field]",
                "  approaches:",
                "    - { id: careful, skill: SKILL_A, difficulty: 5 }",
                "  decision:",
                "    id: DECISION_PROMPT_1",
                "    stakes: 2",
                "    bold: [{ progress: 1 }]",
                "    safe: []",
                "    chronicle: { weight: 0 }"),
            text);
        ContentFileSet files = ContentEdits.ReplaceText(Repo(), "encounters/base.yaml", text);
        EncounterModel read = ContentEdits.Get<EncounterModel>(files, ContentKind.Encounter, "ENCOUNTER_01")!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(model), System.Text.Json.JsonSerializer.Serialize(read));
        Assert.Empty(ContentEdits.Losses(files, "encounters/base.yaml"));
    }

    // The leading comment block is kept as read, with no trailing spaces; blank lines around it go.
    [Fact]
    public void The_leading_comment_block_is_kept_and_the_rest_is_not()
    {
        string text = "\n# Placeholders.   \n#   Second line.\n\n- id: SKILL_A # why\n# inside\n- id: SKILL_B\n";
        Assert.Equal("# Placeholders.\n#   Second line.", ContentWriter.LeadingComment(text));
        Assert.Null(ContentWriter.LeadingComment("- id: SKILL_A\n"));
        Assert.Equal(
            Lines("# Placeholders.", "#   Second line.", "- id: SKILL_A"),
            ContentWriter.Render(ContentWriter.LeadingComment(text), [new SkillModel("SKILL_A", null, [])]));
    }

    [Fact]
    public void An_empty_list_is_written_so_the_loader_still_reads_a_list()
    {
        Assert.Equal("[]\n", Render());
        Assert.Equal("# Note.\n[]\n", ContentWriter.Render("# Note.", []));
    }

    // Rule 2: no trailing spaces, one final newline, and the same bytes the second time.
    [Fact]
    public void Writing_twice_gives_identical_bytes_with_no_trailing_spaces()
    {
        ContentFileSet once = ContentEdits.Canonicalize(Repo());
        ContentFileSet twice = ContentEdits.Canonicalize(once);
        foreach ((string path, string text) in once.Files)
        {
            Assert.Equal(text, twice.Files[path]);
            Assert.EndsWith("\n", text);
            Assert.DoesNotContain(" \n", text);
            Assert.DoesNotContain("\n\n", text);
            Assert.DoesNotContain("\r", text);
        }
    }

    // The repository's own files that need no reformatting stay byte for byte (a first save of an untouched item is no diff).
    [Theory]
    [InlineData("crew/starting.yaml")]
    [InlineData("encounters/base.yaml")]
    [InlineData("objectives/base.yaml")]
    [InlineData("skills/base.yaml")]
    public void A_file_already_in_the_canonical_layout_is_unchanged(string path)
    {
        ContentFileSet files = Repo();
        Assert.Equal(files.Files[path], ContentEdits.Canonicalize(files).Files[path]);
    }

    // Tuning files and the manifest are text, never rewritten by a canonical pass.
    [Fact]
    public void Tuning_files_and_the_manifest_are_left_alone()
    {
        ContentFileSet files = Repo();
        ContentFileSet canonical = ContentEdits.Canonicalize(files);
        foreach (string path in new[] { "pack.yaml", "tuning/calendar.yaml", "tuning/chronicle.yaml", "tuning/crew.yaml", "tuning/missions.yaml", "calendars/base.yaml" })
        {
            Assert.Same(files.Files[path], canonical.Files[path]);
        }
    }

    public static TheoryData<string> AwkwardStrings() =>
    [
        "-dash", "- dash", ": colon", "a: b", "a:b", "#hash", "a #b", "'single'", "\"double\"", "it's", "yes", "no", "Yes", "NO", "null", "Null", "~", "true", "False", "on", "off", "y", "n",
        "123", "1.5", "-1", "0x1F", "1e3", ".inf", " lead", "trail ", "  both  ", "x, y", "[x]", "{x}", "*star", "&anchor", "!tag", "%pct", "@at", "`tick", "?q", "|pipe", ">gt", "=", "tab\there",
        "line\nbreak", "back\\slash", "a\u2028b", "a\u0085b", "caf\u00e9 \u00fcber", "emoji \U0001F680", "a/b (c) d-e.f", "Name placeholder", "x",
    ];

    // Rule 3: whatever the name, it reads back as the same string through the file set and through the loader's own parser.
    [Theory]
    [MemberData(nameof(AwkwardStrings))]
    public void An_awkward_name_reads_back_as_the_same_string(string name)
    {
        ContentFileSet edited = ContentEdits.Upsert(Repo(), ContentKind.Skill, new SkillModel("SKILL_A", name, ["tag " + name]));
        SkillModel back = ContentEdits.Get<SkillModel>(edited, ContentKind.Skill, "SKILL_A")!;
        Assert.Equal(name, back.Name);
        Assert.Equal(["tag " + name], back.Tags);

        // The loader holds a tag to the inner id rule, so it is given a plain tag; the awkward text above proved the file round trip.
        ContentLoadResult result = ContentEdits.Upsert(Repo(), ContentKind.Skill, new SkillModel("SKILL_A", name, ["tag"])).Validate();
        bool loaderAccepts = name.Trim().Length is >= 1 and <= 80 && name.Trim().IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']) < 0;
        if (loaderAccepts)
        {
            Assert.Empty(result.Errors);
            SkillDef skill = result.Pack!.Skills.First(s => s.Id == "SKILL_A");
            Assert.Equal(name.Trim(), skill.Name);
            Assert.Equal(["tag"], skill.Tags);
        }
    }

    // Rule 3 also covers a string that stands alone as a value: the gear tag, the chain, a map key.
    [Theory]
    [MemberData(nameof(AwkwardStrings))]
    public void An_awkward_string_alone_reads_back_the_same(string value)
    {
        ContentFileSet files = new(new Dictionary<string, string>());
        files = ContentEdits.Upsert(files, ContentKind.Gear, new GearModel("GEAR_01", null, value));
        files = ContentEdits.Upsert(files, ContentKind.Crew, new CrewModel("CREW_01", null, 1, new Dictionary<string, int> { [value] = 3 }, 0, 0));
        Assert.Equal(value, ContentEdits.Get<GearModel>(files, ContentKind.Gear, "GEAR_01")!.Tag);
        Assert.Equal(value, Assert.Single(ContentEdits.Get<CrewModel>(files, ContentKind.Crew, "CREW_01")!.Skills).Key);
    }

    [Theory]
    [InlineData("-dash", "\"-dash\"")]
    [InlineData("yes", "\"yes\"")]
    [InlineData("123", "\"123\"")]
    [InlineData("a: b", "\"a: b\"")]
    [InlineData(" lead", "\" lead\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("line\nbreak", "\"line\\nbreak\"")]
    [InlineData("SKILL_A", "SKILL_A")]
    [InlineData("Name placeholder", "Name placeholder")]
    public void Quote_leaves_plain_strings_alone_and_quotes_the_rest(string value, string expected)
    {
        Assert.Equal(expected, ContentWriter.Quote(value));
    }

    // A name that is explicitly null in the file is no name.
    [Fact]
    public void A_null_name_in_the_file_reads_as_no_name_and_is_omitted()
    {
        ContentFileSet files = new(new Dictionary<string, string> { ["gear/base.yaml"] = "- id: GEAR_01\n  name: null\n  tag: scanner\n- id: GEAR_02\n  name:\n  tag: tools\n- id: GEAR_03\n  name: \"null\"\n  tag: comms\n" });
        Assert.Null(ContentEdits.Get<GearModel>(files, ContentKind.Gear, "GEAR_01")!.Name);
        Assert.Null(ContentEdits.Get<GearModel>(files, ContentKind.Gear, "GEAR_02")!.Name);
        Assert.Equal("null", ContentEdits.Get<GearModel>(files, ContentKind.Gear, "GEAR_03")!.Name);
        string text = ContentEdits.Canonicalize(files).Files["gear/base.yaml"];
        Assert.Equal(Lines("- id: GEAR_01", "  tag: scanner", "- id: GEAR_02", "  tag: tools", "- id: GEAR_03", "  name: \"null\"", "  tag: comms"), text);
    }

    // The loader folds several effect keys and several entries together; the writer keeps them as the file had them.
    [Fact]
    public void Effects_written_several_to_an_entry_or_repeated_keep_their_meaning()
    {
        ContentFileSet repo = Repo();
        string yaml = repo.Files["encounters/base.yaml"].Replace("bold: [{ momentum: 1 }, { strain: 1 }]\n    safe: []", "bold: [{ momentum: 1, strain: 1 }, { momentum: 2 }, { withdraw: true }]\n    safe: [{ injuryRoll: severe }]");
        Assert.NotEqual(repo.Files["encounters/base.yaml"], yaml);
        ContentFileSet one = ContentEdits.ReplaceText(repo, "encounters/base.yaml", yaml);
        ContentFileSet two = ContentEdits.Canonicalize(one);
        EncounterDef before = one.Validate().Pack!.Encounters.Single(e => e.Id == "ENCOUNTER_01");
        EncounterDef after = two.Validate().Pack!.Encounters.Single(e => e.Id == "ENCOUNTER_01");
        Assert.Equal(new EffectDef(Momentum: 3, Strain: 1, Withdraw: true), before.Decision!.Bold);
        Assert.Equal(before.Decision.Bold, after.Decision!.Bold);
        Assert.Equal(before.Decision.Safe, after.Decision.Safe);
    }
}

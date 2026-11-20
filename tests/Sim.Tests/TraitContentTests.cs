using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

// Trait definitions in content/traits/ (slice 1e, part three, design 4.3) and every use of a trait checked against them.
public class TraitContentTests
{
    private const string Stood = """
        - id: trait_x
          on: crew
          levels: { 1: level_a, 2: level_b }
          briefing: BRIEFING_X
          launch:
            sent: [{ set: level_b }, { loyalty: -2 }, { morale: -4 }]
            notSent: [{ set: 0 }]
          expires: { weeks: 6 }

        """;

    private static ContentLoadResult LoadWith(string traits, Func<Dictionary<string, string>, Dictionary<string, string>>? change = null)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        // Beside the fixture's own, which its storylets use.
        files["traits/base.yaml"] += traits;
        return ContentLoader.Load(change is null ? files : change(files));
    }

    private static void AssertRefused(string traits, string message, string file = "traits/base.yaml", Func<Dictionary<string, string>, Dictionary<string, string>>? change = null)
    {
        ContentLoadResult result = LoadWith(traits, change);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == file && e.Message == message);
    }

    [Fact]
    public void Both_packs_load_their_traits()
    {
        Assert.Equal(["trait_a", "trait_c", "trait_b", "trait_e", "trait_f", "trait_g", "trait_h", "trait_i", "trait_j", "trait_k"], TestContent.Pack.Traits.Select(t => t.Id));
        ContentLoadResult game = ContentLoader.LoadDirectory(Path.Combine(TestContent.Root, "content"));
        Assert.Empty(game.Errors);
        Assert.Contains(game.Pack!.Traits, t => t.Id == "stood_down");
    }

    [Fact]
    public void A_definition_loads_with_its_levels_resolved()
    {
        TraitDef def = LoadWith(Stood).Pack!.Traits.Single(t => t.Id == "trait_x");

        Assert.Equal((TraitPlaces.Crew, true, 2, "BRIEFING_X", 6), (def.On, def.Shown, def.Cap, def.Briefing, def.ExpiresWeeks));
        Assert.Equal("level_b", def.LevelName(2));
        Assert.Equal(
            [new TraitRuleEffect(null, 2, true, 0, 0, "holder"), new TraitRuleEffect(null, 0, false, -2, 0, "holder"), new TraitRuleEffect(null, 0, false, 0, -4, "holder")],
            def.Launch!.Sent);
        Assert.Equal([new TraitRuleEffect(null, 0, true, 0, 0, "holder")], def.Launch.NotSent);
    }

    [Theory]
    [InlineData("- id: trait_x\n  on: deck\n", "trait_x: on must be ship, crew, or pair, or left out for the ship or a crew member.")]
    [InlineData("- id: trait_x\n  levels: { 0: none }\n", "trait_x: A level's number is 1 or more; 0 is not.")]
    [InlineData("- id: trait_x\n  levels: { 1: Bad }\n", "trait_x: Level name 'Bad' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("- id: trait_x\n  levels: { 1: same, 2: same }\n", "trait_x: Level name 'same' is used twice.")]
    [InlineData("- id: trait_x\n  max: 0\n", "trait_x: max must be 1 or more.")]
    [InlineData("- id: trait_x\n  max: 1\n  levels: { 1: a, 2: b }\n", "trait_x: max 1 is below its highest level, 2.")]
    [InlineData("- id: trait_x\n  briefing: \" padded \"\n", "trait_x: briefing must be 1 to 40 characters on one line, with no spaces at either end.")]
    [InlineData("- id: trait_x\n  every: { weeks: 0, add: -1 }\n", "trait_x: every needs weeks of 1 or more and an add that is not 0.")]
    [InlineData("- id: trait_x\n  levels: { 1: a }\n  every: { weeks: 2, add: -1 }\n", "trait_x: A trait with named levels is set, not added to, so it cannot have every.")]
    [InlineData("- id: trait_x\n  every: { weeks: 2, add: 1 }\n", "trait_x: A trait that rises every week needs a max.")]
    [InlineData("- id: trait_x\n  expires: { weeks: 0 }\n", "trait_x: expires needs weeks of 1 or more.")]
    [InlineData("- id: trait_x\n  mission: { checkBonus: 0 }\n", "trait_x: mission needs a checkBonus that is not 0.")]
    [InlineData("- id: trait_x\n  mission: { checkBonus: 1, withTarget: true }\n", "trait_x: Only a trait on a pair can have withTarget.")]
    [InlineData("- id: trait_x\n  launch: { withTarget: [{ morale: -1 }] }\n", "trait_x: Only a trait on a pair can have withTarget.")]
    [InlineData("- id: trait_x\n  onTargetDied: [{ set: 0 }]\n", "trait_x: Only a trait on a pair can have onTargetDied.")]
    [InlineData("- id: trait_x\n  launch: { sent: [{ set: 1, morale: 1 }] }\n", "trait_x: launch.sent: A rule's effect changes one thing: a trait (set or add), loyalty, or morale.")]
    [InlineData("- id: trait_x\n  launch: { sent: [{ morale: 0 }] }\n", "trait_x: launch.sent: A morale change must be a whole number that is not 0.")]
    [InlineData("- id: trait_x\n  launch: { sent: [{ morale: -1, on: target }] }\n", "trait_x: launch.sent: Only a trait on a pair has a target.")]
    [InlineData("- id: trait_x\n  launch: { sent: [{ set: 0, on: crew }] }\n", "trait_x: launch.sent: on: crew is for loyalty and morale; a trait is changed on the holder or the target.")]
    [InlineData("- id: trait_x\n  levels: { 1: a, 2: b }\n  launch: { notSent: [{ add: -1 }] }\n", "trait_x: launch.notSent: trait_x has named levels, so it is set, not added to.")]
    [InlineData("- id: trait_x\n  levels: { 1: a, 2: b }\n  launch: { sent: [{ set: c }] }\n", "trait_x: launch.sent: trait_x has no level named 'c'; its levels are a and b.")]
    [InlineData("- id: Trait_X\n", "Id 'Trait_X' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    public void A_definition_outside_the_rules_is_refused(string traits, string message)
    {
        AssertRefused(traits, message);
    }

    [Fact]
    public void A_level_name_resolves_in_effects_and_requirements_everywhere()
    {
        ContentPack pack = LoadWith(Stood, files =>
        {
            files["storylets/base.yaml"] = files["storylets/base.yaml"]
                .Replace("subject.trait.trait_c == 0", "subject.trait.trait_x == 'level_a'")
                .Replace("effects: [{ trait: trait_c, set: 2 }, { loyalty: -2, on: subject }]", "effects: [{ trait: trait_x, set: level_b }, { loyalty: -2, on: subject }]");
            files["encounters/base.yaml"] = files["encounters/base.yaml"].Replace("{ trait: trait_b, add: 1, on: actor }", "{ trait: trait_x, set: level_a, on: actor }");
            return files;
        }).Pack!;

        StoryletDef storylet = pack.Storylets.Single(s => s.Id == "STORYLET_02");
        Assert.Contains(new TraitRead("trait_x", "subject", null), storylet.Reads);
        Assert.Contains(new TraitRead("trait_x", "subject", null), storylet.Needs);
        Assert.Equal(new TraitChange("trait_x", 2, true, ""), storylet.Options[1].Effects[0].Trait);
        Assert.Equal([new TraitChange("trait_x", 1, true, "actor")], pack.Encounters.Single(e => e.Id == "ENCOUNTER_04").Approaches[0].Outcomes["disaster"].Traits!);
    }

    [Fact]
    public void An_add_to_a_trait_with_levels_is_refused_wherever_it_is_written()
    {
        string message = "trait_x has named levels, so it is set, not added to.";
        AssertRefused(Stood, $"ENCOUNTER_04: {message}", "encounters/base.yaml", files =>
        {
            files["encounters/base.yaml"] = files["encounters/base.yaml"].Replace("{ trait: trait_b, add: 1, on: actor }", "{ trait: trait_x, add: 1, on: actor }");
            return files;
        });
        AssertRefused(Stood, $"STORYLET_02: {message}", "storylets/base.yaml", files =>
        {
            files["storylets/base.yaml"] = files["storylets/base.yaml"].Replace("{ trait: trait_b, add: -1 }", "{ trait: trait_x, add: -1 }");
            return files;
        });
    }

    [Fact]
    public void A_level_name_that_is_not_one_is_refused()
    {
        AssertRefused(Stood, "STORYLET_02: trait_x has no level named 'level_c'; its levels are level_a and level_b.", "storylets/base.yaml", files =>
        {
            files["storylets/base.yaml"] = files["storylets/base.yaml"].Replace("{ trait: trait_c, set: 2 }", "{ trait: trait_x, set: level_c }");
            return files;
        });

        // In an expression (1e part four), the level is checked by the expression book, with its position.
        AssertRefused(Stood, "STORYLET_02: cast.subject, at character 56: trait_x has no level named level_c; its levels are level_a, level_b.", "storylets/base.yaml", files =>
        {
            files["storylets/base.yaml"] = files["storylets/base.yaml"].Replace("subject.trait.trait_c == 0", "subject.trait.trait_x == 'level_c'");
            return files;
        });
    }

    [Fact]
    public void A_use_where_the_definition_does_not_put_the_trait_is_refused()
    {
        AssertRefused(Stood, "ENCOUNTER_01: trait_x sits on a crew member, not on the ship.", "encounters/base.yaml", files =>
        {
            files["encounters/base.yaml"] = files["encounters/base.yaml"].Replace("{ trait: trait_a, add: 1, on: ship }", "{ trait: trait_x, set: 1, on: ship }");
            return files;
        });
        AssertRefused(Stood, "STORYLET_01: trait_x sits on a crew member, not on the ship.", "storylets/base.yaml", files =>
        {
            files["storylets/base.yaml"] = files["storylets/base.yaml"].Replace("{ trait: trait_a, set: 2 }", "{ trait: trait_x, set: 1 }");
            return files;
        });
        AssertRefused(Stood, "STORYLET_01: requires, at character 12: trait_x is on crew, not the ship.", "storylets/base.yaml", files =>
        {
            files["storylets/base.yaml"] = files["storylets/base.yaml"].Replace("ship.trait.trait_a == 1", "ship.trait.trait_x == 1");
            return files;
        });
    }

    // No hidden memory, with a cap: an option that sets a trait past its max is clamped before the check, so a storylet whose only
    // way out is to push the trait past its cap is still one that would return.
    [Fact]
    public void An_option_is_judged_on_the_value_the_cap_leaves()
    {
        const string traits = "- id: trait_x\n  max: 2\n";
        AssertRefused(traits, "STORYLET_X: Option 'option_b' leaves every expression true, so this storylet would come back every cycle.", "storylets/base.yaml", files =>
        {
            files["storylets/base.yaml"] = """
                - id: STORYLET_X
                  stakes: 2
                  requires: "ship.trait.trait_x >= 2"
                  options:
                    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_x, set: 0 }] }
                    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_x, set: 5 }] }

                """;
            return files;
        });
    }

    // --- Pairs (1e part three, Task 3) ---

    private static Func<Dictionary<string, string>, Dictionary<string, string>> Encounter(string from, string to)
    {
        return files =>
        {
            Assert.Contains(from, files["encounters/base.yaml"]);
            files["encounters/base.yaml"] = files["encounters/base.yaml"].Replace(from, to);
            return files;
        };
    }

    private static Func<Dictionary<string, string>, Dictionary<string, string>> Storylet(string from, string to)
    {
        return files =>
        {
            Assert.Contains(from, files["storylets/base.yaml"]);
            files["storylets/base.yaml"] = files["storylets/base.yaml"].Replace(from, to);
            return files;
        };
    }

    private const string Disaster = "{ trait: trait_b, add: 1, on: actor }";

    [Theory]
    [InlineData("{ trait: trait_g, add: 1, on: team, toward: lead }", "ENCOUNTER_04: toward names lead or actor, with on: actor or on: lead, and never the same word as on.")]
    [InlineData("{ trait: trait_g, add: 1, on: actor, toward: actor }", "ENCOUNTER_04: toward names lead or actor, with on: actor or on: lead, and never the same word as on.")]
    [InlineData("{ trait: trait_g, add: 1, on: actor, toward: team }", "ENCOUNTER_04: toward names lead or actor, with on: actor or on: lead, and never the same word as on.")]
    [InlineData("{ trait: trait_g, add: 1, on: actor }", "ENCOUNTER_04: trait_g sits on a pair, so it is used with toward.")]
    [InlineData("{ trait: trait_b, add: 1, on: actor, toward: lead }", "ENCOUNTER_04: A trait on a pair needs a definition with on: pair.")]
    [InlineData("{ trait: trait_z, add: 1, on: actor, toward: lead }", "ENCOUNTER_04: A trait on a pair needs a definition with on: pair.")]
    public void A_pair_in_an_encounter_outside_the_rules_is_refused(string effect, string message)
    {
        AssertRefused("", message, "encounters/base.yaml", Encounter(Disaster, effect));
    }

    [Fact]
    public void Toward_in_a_decision_is_refused()
    {
        AssertRefused("", "ENCOUNTER_01: toward is only for an approach's outcome, where there is an actor.", "encounters/base.yaml",
            Encounter("bold: [{ momentum: 1 }, { strain: 1 }]", "bold: [{ momentum: 1 }, { trait: trait_g, add: 1, on: lead, toward: actor }]"));
    }

    [Fact]
    public void A_pair_storylet_loads_with_its_target()
    {
        StoryletDef pair = TestContent.Pack.Storylets.Single(s => s.Id == "STORYLET_03");

        Assert.Equal(["subject", "target"], pair.Slots);
        Assert.Equal(new TraitChange("trait_g", 0, true, "", StoryletScopes.Target), pair.Options[0].Effects[0].Trait);
        Assert.Equal(["subject", "target"], TestContent.Pack.Vignettes.Single(v => v.Id == "VIGNETTE_04").Slots);
    }

    [Theory]
    [InlineData("{ loyalty: -1, on: crew }", "{ loyalty: -1, on: target }", "STORYLET_01: on: target names no slot; this storylet casts no one.")]
    [InlineData("{ trait: trait_a, set: 2 }", "{ trait: trait_a, set: 2, toward: target }", "STORYLET_01: toward: target names no slot; this storylet casts no one.")]
    [InlineData("subject.trait.trait_c == 0", "subject.trait.trait_c == 0 && target.morale <= 20", "STORYLET_02: cast.subject, at character 61: target is not a slot this expression can read; it reads subject, ship, cycle, storylet, and chronicle.")]
    [InlineData("{ trait: trait_g, set: 5, toward: target }", "{ trait: trait_g, set: 5, on: target, toward: target }", "STORYLET_03: toward: target names the slot the trait is on; a pair trait is held toward someone else.")]
    [InlineData("{ trait: trait_g, set: 5, toward: target }", "{ trait: trait_g, set: 5 }", "STORYLET_03: trait_g sits on a pair, so it is used with toward.")]
    [InlineData("{ trait: trait_g, set: 5, toward: target }", "{ trait: trait_b, set: 1, toward: target }", "STORYLET_03: A trait on a pair needs a definition with on: pair.")]
    public void A_pair_in_a_storylet_outside_the_rules_is_refused(string from, string to, string message)
    {
        AssertRefused("", message, "storylets/base.yaml", Storylet(from, to));
    }
}

using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class StoryletContentTests
{
    private const string Storylet = """
        - id: STORYLET_X
          stakes: 2
          scope: crew
          requires:
            - { trait: trait_a, min: 1, max: 1 }
          options:
            - id: option_a
              label: LABEL_A
              effects: [{ trait: trait_a, set: 0 }]
            - id: option_b
              label: LABEL_B
              effects: [{ trait: trait_a, set: 2 }]

        """;

    private static ContentLoadResult LoadWith(string storylets, string? tuning = null)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["storylets/base.yaml"] = storylets;
        if (tuning is not null)
        {
            files["tuning/missions.yaml"] = tuning;
        }

        return ContentLoader.Load(files);
    }

    private static void AssertRefused(string storylets, string message)
    {
        ContentLoadResult result = LoadWith(storylets);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == "storylets/base.yaml" && e.Message == message);
    }

    [Fact]
    public void The_fixture_storylets_load()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Equal(["STORYLET_01", "STORYLET_02", "STORYLET_03"], pack.Storylets.Select(s => s.Id));
        Assert.Equal(2, pack.StoryletsPerCycle);
        StoryletDef ship = pack.Storylets[0];
        Assert.Equal((2, StoryletScopes.Ship, (string?)null), (ship.Stakes, ship.Scope, ship.Name));
        Assert.Equal([new TraitRange("trait_a", 1, 1)], ship.Requires);
        Assert.Equal(["option_a", "option_b"], ship.Options.Select(o => o.Id));
        Assert.Equal("STORYLET_01_OPTION_A_LABEL", ship.Options[0].Label);
        Assert.Equal([new StoryletEffect(Trait: new TraitChange("trait_a", 0, true, "")), new StoryletEffect(Loyalty: -1, LoyaltyOn: "crew")], ship.Options[0].Effects);
        StoryletDef crew = pack.Storylets[1];
        Assert.Equal(StoryletScopes.Crew, crew.Scope);
        Assert.Equal([new TraitRange("trait_b", 1, null), new TraitRange("trait_c", 0, 0)], crew.Requires);
        Assert.Equal(
            [
                new StoryletEffect(Trait: new TraitChange("trait_b", -1, false, "")),
                new StoryletEffect(Trait: new TraitChange("trait_c", 1, true, "")),
                new StoryletEffect(Trait: new TraitChange("trait_e", 1, true, "")),
            ],
            crew.Options[0].Effects);
        Assert.Equal([new StoryletEffect(Trait: new TraitChange("trait_c", 2, true, "")), new StoryletEffect(Loyalty: -2, LoyaltyOn: "subject")], crew.Options[1].Effects);
    }

    [Fact]
    public void A_storylets_text_and_its_options_text_are_lines_of_the_pack_with_their_implied_criteria()
    {
        List<LineDef> owned = TestContent.Pack.Lines.Where(l => l.Owner == "STORYLET_02").ToList();

        Assert.Equal(["STORYLET_02.text", "STORYLET_02.option_a.text", "STORYLET_02.option_b.text"], owned.Select(l => l.Id));
        Assert.Equal(["storylet", "storyletAnswer", "storyletAnswer"], owned.Select(l => l.Beat));
        Assert.Equal(["storylet=STORYLET_02"], owned[0].When.Select(c => $"{c.Fact}={c.Values.Single()}"));
        Assert.Equal(["storylet=STORYLET_02", "option=option_b"], owned[2].When.Select(c => $"{c.Fact}={c.Values.Single()}"));
        Assert.StartsWith("subject: STORYLET_02_LINE_1\n", owned[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pool_line_may_test_a_storylet_the_pack_has_and_no_other()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["lines/storylet.yaml"] = "- id: LINE_STORYLET\n  when: { beat: storylet, storylet: STORYLET_01 }\n  text: \"#storylet# held.\"\n"
            + "- id: LINE_STORYLET_OTHER\n  when: { beat: storyletAnswer, storylet: STORYLET_09, option: option_a }\n  text: x\n";

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Equal(["LINE_STORYLET_OTHER: storylet names 'STORYLET_09', which the pack does not have."], result.Errors.Select(e => e.Message));
    }

    [Fact]
    public void A_storylet_with_no_words_loads()
    {
        ContentLoadResult result = LoadWith(Storylet);

        Assert.Empty(result.Errors);
        Assert.DoesNotContain(result.Pack!.Lines, l => l.Owner == "STORYLET_X");
    }

    [Fact]
    public void A_storylets_name_loads_trimmed_and_a_bad_one_is_refused()
    {
        ContentLoadResult named = LoadWith(Storylet.Replace("  stakes: 2\n", "  name: \" STORYLET_X_NAME \"\n  stakes: 2\n"));
        ContentLoadResult result = LoadWith(Storylet.Replace("  stakes: 2\n", "  name: \"\"\n  stakes: 2\n"));

        Assert.Equal("STORYLET_X_NAME", named.Pack!.Storylets.Single().Name);
        Assert.Contains(result.Errors, e => e.Message == "STORYLET_X: name must be 1 to 80 characters with no line break.");
    }

    [Theory]
    [InlineData("stakes: 2", "stakes: 0")]
    [InlineData("stakes: 2", "stakes: 6")]
    public void Stakes_outside_one_to_five_are_refused(string from, string to)
    {
        AssertRefused(Storylet.Replace(from, to), "STORYLET_X: A storylet's stakes must be 1 to 5.");
    }

    [Theory]
    [InlineData("  scope: crew\n", "")]
    [InlineData("scope: crew", "scope: team")]
    public void A_scope_other_than_ship_crew_or_pair_is_refused(string from, string to)
    {
        AssertRefused(Storylet.Replace(from, to), "STORYLET_X: A storylet needs scope: ship, crew, or pair.");
    }

    [Theory]
    [InlineData("requires: []")]
    [InlineData("requires: [{ trait: trait_a }, { trait: trait_b }, { trait: trait_c }, { trait: trait_d }, { trait: trait_e }]")]
    public void No_requirement_or_more_than_four_is_refused(string requires)
    {
        string storylet = Storylet.Replace("requires:\n    - { trait: trait_a, min: 1, max: 1 }", requires);

        AssertRefused(storylet, "STORYLET_X: A storylet needs one to four requirements.");
    }

    [Theory]
    [InlineData("{ trait: Trait_A, min: 1 }", "STORYLET_X: Trait id 'Trait_A' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("{ min: 1 }", "STORYLET_X: Trait id '' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("{ trait: trait_a, over: 1 }", "STORYLET_X: A requirement takes trait, stat, or last; 'over' is not one of them.")]
    [InlineData("{ trait: trait_a, min: one }", "STORYLET_X: A requirement's min and max must be whole numbers of 0 or more.")]
    [InlineData("{ trait: trait_a, min: -1 }", "STORYLET_X: A requirement's min and max must be whole numbers of 0 or more.")]
    [InlineData("{ trait: trait_a, min: 2, max: 1 }", "STORYLET_X: The requirement on trait_a has max 1 below min 2.")]
    [InlineData("{ trait: trait_a, max: 1, min: 3 }", "STORYLET_X: The requirement on trait_a has max 1 below min 3.")]
    public void A_requirement_that_is_not_a_trait_and_a_range_is_refused(string requirement, string message)
    {
        AssertRefused(Storylet.Replace("{ trait: trait_a, min: 1, max: 1 }", requirement), message);
    }

    [Fact]
    public void A_requirement_with_only_a_max_starts_at_zero_and_one_with_neither_at_one()
    {
        string storylet = Storylet.Replace("{ trait: trait_a, min: 1, max: 1 }", "{ trait: trait_a, max: 1 }\n    - { trait: trait_b }");

        // Each option must still move a requirement out of range: option_a raises trait_a past its max, option_b clears trait_b.
        storylet = storylet.Replace("{ trait: trait_a, set: 2 }", "{ trait: trait_b, set: 0 }").Replace("{ trait: trait_a, set: 0 }", "{ trait: trait_a, set: 2 }");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
        Assert.Equal([new TraitRange("trait_a", 0, 1), new TraitRange("trait_b", 1, null)], result.Pack!.Storylets.Single().Requires);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Fewer_than_two_or_more_than_four_options_are_refused(int count)
    {
        string options = string.Concat(Enumerable.Range(1, count).Select(n => $"    - {{ id: option_{n}, label: L, effects: [{{ trait: trait_a, set: 0 }}] }}\n"));
        string storylet = Storylet[..Storylet.IndexOf("  options:", StringComparison.Ordinal)] + "  options:\n" + options;

        AssertRefused(storylet, "STORYLET_X: A storylet needs two to four options.");
    }

    [Fact]
    public void A_blank_option_is_refused()
    {
        AssertRefused(Storylet.Replace("  options:\n", "  options:\n    -\n"), "STORYLET_X: options[1] is blank.");
    }

    [Fact]
    public void Requirements_on_one_trait_that_no_value_satisfies_are_refused()
    {
        AssertRefused(
            Storylet.Replace("{ trait: trait_a, min: 1, max: 1 }", "{ trait: trait_a, min: 1, max: 1 }\n    - { trait: trait_a, min: 2 }"),
            "STORYLET_X: No value of trait_a satisfies all of this storylet's requirements on it.");
    }

    [Theory]
    [InlineData("id: option_b", "id: Option_B", "STORYLET_X: Option id 'Option_B' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("id: option_b", "id: option_a", "STORYLET_X: Option id 'option_a' is used twice.")]
    [InlineData("label: LABEL_B", "label: \" LABEL_B\"", "STORYLET_X: Option 'option_b': label must be 1 to 40 characters on one line, with no spaces at either end.")]
    [InlineData("label: LABEL_B", "label: \"\"", "STORYLET_X: Option 'option_b': label must be 1 to 40 characters on one line, with no spaces at either end.")]
    [InlineData("label: LABEL_B", "label: AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "STORYLET_X: Option 'option_b': label must be 1 to 40 characters on one line, with no spaces at either end.")]
    public void An_option_with_a_bad_id_or_label_is_refused(string from, string to, string message)
    {
        AssertRefused(Storylet.Replace(from, to), message);
    }

    [Theory]
    [InlineData("{ momentum: 1 }", "momentum")]
    [InlineData("{ loyalty: 1, on: crew, strain: 2 }", "strain")]
    [InlineData("{ trait: trait_b, add: 1, injuryRoll: light }", "injuryRoll")]
    public void An_effect_other_than_loyalty_or_a_trait_is_refused_by_name(string effect, string key)
    {
        string storylet = Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", $"effects: [{{ trait: trait_a, set: 2 }}, {effect}]");

        AssertRefused(storylet, $"STORYLET_X: A storylet's effect can change loyalty, morale, or a trait; '{key}' is not one of them.");
    }

    [Theory]
    [InlineData("{ loyalty: -1 }", "STORYLET_X: A loyalty change needs on: subject or crew.")]
    [InlineData("{ loyalty: -1, on: ship }", "STORYLET_X: A loyalty change needs on: subject or crew.")]
    [InlineData("{ loyalty: lots, on: crew }", "STORYLET_X: A loyalty change must be a whole number.")]
    [InlineData("{ trait: trait_b, add: 1, on: crew }", "STORYLET_X: A storylet's trait change takes on: ship, subject, or target, or no on.")]
    [InlineData("{ trait: trait_b, add: 1, set: 1 }", "STORYLET_X: A trait change needs add or set, and only one of them.")]
    [InlineData("{ trait: trait_b }", "STORYLET_X: A trait change needs add or set, and only one of them.")]
    [InlineData("{ trait: trait_b, add: lots }", "STORYLET_X: A trait change's amount must be a whole number.")]
    [InlineData("{ trait: trait_b, set: abc }", "STORYLET_X: A trait change's amount must be a whole number.")]
    [InlineData("{}", "STORYLET_X: A storylet's effect is blank.")]
    [InlineData("{ trait: trait_b, set: -1 }", "STORYLET_X: A trait's set must be 0 or more.")]
    [InlineData("{ trait: Trait_B, add: 1 }", "STORYLET_X: Trait id 'Trait_B' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("{ trait: trait_b, add: 1, loyalty: 1 }", "STORYLET_X: An effect changes one thing; this one changes loyalty and trait.")]
    public void A_malformed_loyalty_or_trait_change_is_refused(string effect, string message)
    {
        string storylet = Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", $"effects: [{{ trait: trait_a, set: 2 }}, {effect}]");

        AssertRefused(storylet, message);
    }

    [Theory]
    [InlineData("{ loyalty: -1, on: subject }")]
    [InlineData("{ trait: trait_b, add: 1, on: subject }")]
    public void A_storylet_for_the_ship_cannot_change_a_subject(string effect)
    {
        string storylet = Storylet.Replace("scope: crew", "scope: ship").Replace("effects: [{ trait: trait_a, set: 2 }]", $"effects: [{{ trait: trait_a, set: 2 }}, {effect}]");

        AssertRefused(storylet, "STORYLET_X: A storylet for the ship has no subject, so nothing can be on: subject.");
    }

    [Theory]
    [InlineData("  text: \"subject: Hello.\"\n", "STORYLET_X: text cannot be used: speaker 'subject' is not in a storylet beat, which has no one.")]
    [InlineData("  text: \"#subject# waited.\"\n", "STORYLET_X: text cannot be used: #subject# names 'subject', which a storylet beat does not have.")]
    public void A_storylet_for_the_ship_cannot_name_a_subject_in_its_text(string text, string message)
    {
        string storylet = Storylet.Replace("scope: crew\n", "scope: ship\n" + text);

        AssertRefused(storylet, message);
    }

    [Fact]
    public void A_storylet_for_crew_may_name_its_subject_and_the_best_at_a_skill()
    {
        string storylet = Storylet.Replace("scope: crew\n", "scope: crew\n  text: |-\n    subject: #subject.they# asked.\n    best SKILL_A: Noted.\n    = #subject# asked about #storylet#.\n")
            .Replace("label: LABEL_A\n", "label: LABEL_A\n      text: \"#subject# was heard.\"\n");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("  text: \"lead: Hello.\"\n", "STORYLET_X: text cannot be used: speaker 'lead' is not in a storylet beat, which has subject.")]
    [InlineData("  text: \"#actor# waited.\"\n", "STORYLET_X: text cannot be used: #actor# names 'actor', which a storylet beat does not have.")]
    [InlineData("  text: \"best SKILL_Z: Hello.\"\n", "STORYLET_X: text cannot be used: speaker 'best SKILL_Z' names 'SKILL_Z', a skill the pack does not have.")]
    [InlineData("  text: \"= Only a report.\"\n", "STORYLET_X: text cannot be read: A script needs at least one line besides the report's sentence. Write - alone for a beat that says nothing.")]
    [InlineData("  text: \"\"\n", "STORYLET_X: text must not be empty.")]
    public void A_storylets_text_that_cannot_work_is_refused(string text, string message)
    {
        AssertRefused(Storylet.Replace("scope: crew\n", "scope: crew\n" + text), message);
    }

    [Fact]
    public void An_options_text_that_cannot_work_is_refused()
    {
        AssertRefused(
            Storylet.Replace("label: LABEL_A\n", "label: LABEL_A\n      text: \"#lead# nodded.\"\n"),
            "STORYLET_X: option_a.text cannot be used: #lead# names 'lead', which a storyletAnswer beat does not have.");
    }

    [Theory]
    [InlineData("{ trait: trait_a, set: 1 }")]
    [InlineData("{ trait: trait_a, add: 0 }")]
    [InlineData("{ trait: trait_b, set: 3 }")]
    [InlineData("{ loyalty: -1, on: crew }")]
    [InlineData("{ trait: trait_a, set: 0, on: ship }")]
    public void An_option_that_leaves_every_requirement_true_is_refused(string effect)
    {
        AssertRefused(
            Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", $"effects: [{effect}]"),
            "STORYLET_X: Option 'option_b' leaves every requirement true, so this storylet would come back every cycle.");
    }

    [Fact]
    public void An_option_that_moves_one_requirement_out_of_range_is_enough()
    {
        string storylet = Storylet.Replace("{ trait: trait_a, min: 1, max: 1 }", "{ trait: trait_a, min: 1, max: 1 }\n    - { trait: trait_b, max: 0 }")
            .Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_b, add: 1, on: subject }]");

        Assert.Empty(LoadWith(storylet).Errors);
    }

    [Theory]
    [InlineData("storylets:\n  perCycle: 2", "", "storylets.perCycle is missing.")]
    [InlineData("perCycle: 2", "perCycle: -1", "storylets.perCycle must be a whole number of 0 or more, not '-1'.")]
    public void The_storylets_per_cycle_is_a_required_tuning_value_of_0_or_more(string from, string to, string message)
    {
        string tuning = TestContent.TuningText.Replace(from, to);

        ContentLoadResult result = LoadWith(Storylet, tuning);

        Assert.Contains(result.Errors, e => e.File == "tuning/missions.yaml" && e.Message == message);
    }

    // 1e part two (design 4.2): requirements on the subject's stats and on facts about the last cycle, and morale as an effect.
    [Fact]
    public void A_stat_and_a_fact_requirement_load_beside_a_trait()
    {
        string storylet = Storylet.Replace(
            "    - { trait: trait_a, min: 1, max: 1 }",
            "    - { trait: trait_a, min: 1, max: 1 }\n    - { stat: morale, max: 25 }\n    - { stat: loyalty, min: 60 }\n    - { last: hurt, not: true }");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
        Assert.Equal<Requirement>(
            [new TraitRange("trait_a", 1, 1), new StatRange("morale", 0, 25), new StatRange("loyalty", 60, null), new LastCycle("hurt", true)],
            result.Pack!.Storylets.Single().Requires);
    }

    [Fact]
    public void A_morale_change_loads_on_the_subject_or_the_crew()
    {
        string storylet = Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_a, set: 2 }, { morale: 3, on: subject }, { morale: -1, on: crew }]");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
        Assert.Equal(
            [new StoryletEffect(Morale: 3, MoraleOn: "subject"), new StoryletEffect(Morale: -1, MoraleOn: "crew")],
            result.Pack!.Storylets.Single().Options[1].Effects.Skip(1));
    }

    [Theory]
    [InlineData("{ stat: courage, min: 1 }", "STORYLET_X: 'courage' is not a stat a storylet can read; they are morale, loyalty.")]
    [InlineData("{ stat: morale, over: 1 }", "STORYLET_X: A requirement on stat takes min, max, of; 'over' is not one of them.")]
    [InlineData("{ stat: morale, min: 50, max: 40 }", "STORYLET_X: The requirement on morale has max 40 below min 50.")]
    [InlineData("{ last: promoted }", "STORYLET_X: 'promoted' is not a fact about the last cycle; they are sent, hurt, rested, lost_crewmate.")]
    [InlineData("{ last: sent, not: maybe }", "STORYLET_X: A requirement's not must be true or false.")]
    [InlineData("{ last: sent, min: 1 }", "STORYLET_X: A requirement on last takes not, of; 'min' is not one of them.")]
    public void A_malformed_stat_or_fact_requirement_is_refused(string requirement, string message)
    {
        AssertRefused(Storylet.Replace("    - { trait: trait_a, min: 1, max: 1 }", $"    - {{ trait: trait_a, min: 1, max: 1 }}\n    - {requirement}"), message);
    }

    [Theory]
    [InlineData("{ stat: morale, max: 25 }", "morale")]
    [InlineData("{ last: rested }", "rested")]
    public void A_storylet_for_the_ship_cannot_read_a_stat_or_a_fact(string requirement, string name)
    {
        string storylet = Storylet.Replace("scope: crew", "scope: ship").Replace("    - { trait: trait_a, min: 1, max: 1 }", $"    - {{ trait: trait_a, min: 1, max: 1 }}\n    - {requirement}");

        AssertRefused(storylet, $"STORYLET_X: A storylet for the ship has no subject, so it cannot read {name}.");
    }

    [Fact]
    public void A_storylet_with_no_trait_among_its_requirements_is_refused()
    {
        AssertRefused(Storylet.Replace("    - { trait: trait_a, min: 1, max: 1 }", "    - { stat: morale, max: 25 }"), "STORYLET_X: A storylet needs at least one trait among its requirements, so its answers can stop it returning.");
    }

    [Theory]
    [InlineData("{ morale: 1 }", "STORYLET_X: A morale change needs on: subject or crew.")]
    [InlineData("{ morale: lots, on: crew }", "STORYLET_X: A morale change must be a whole number.")]
    [InlineData("{ morale: 1, loyalty: 1, on: crew }", "STORYLET_X: An effect changes one thing; this one changes loyalty and morale.")]
    public void A_malformed_morale_change_is_refused(string effect, string message)
    {
        AssertRefused(Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", $"effects: [{{ trait: trait_a, set: 2 }}, {effect}]"), message);
    }

    // Slice 1e, part three: flags are traits, and the old key is refused with a pointer to the decision, in storylets and encounters alike.
    [Fact]
    public void Loader_refuses_the_old_trait_key()
    {
        AssertRefused(Storylet.Replace("{ trait: trait_a, min: 1, max: 1 }", "{ flag: trait_a, min: 1, max: 1 }"), "STORYLET_X: 'flag' is now 'trait' (GDD section 15, item 40).");
        AssertRefused(Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ flag: trait_a, set: 2 }]"), "STORYLET_X: 'flag' is now 'trait' (GDD section 15, item 40).");

        Dictionary<string, string> files = TestContent.RepoFiles();
        files["encounters/base.yaml"] = files["encounters/base.yaml"].Replace("{ trait: trait_a, add: 1, on: ship }", "{ flag: trait_a, add: 1, on: ship }");
        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == "encounters/base.yaml" && e.Message.EndsWith("'flag' is now 'trait' (GDD section 15, item 40).", StringComparison.Ordinal));
    }
}

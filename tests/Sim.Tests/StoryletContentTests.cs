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
            - { flag: flag_a, min: 1, max: 1 }
          options:
            - id: option_a
              label: LABEL_A
              effects: [{ flag: flag_a, set: 0 }]
            - id: option_b
              label: LABEL_B
              effects: [{ flag: flag_a, set: 2 }]

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

        Assert.Equal(["STORYLET_01", "STORYLET_02"], pack.Storylets.Select(s => s.Id));
        Assert.Equal(2, pack.StoryletsPerCycle);
        StoryletDef ship = pack.Storylets[0];
        Assert.Equal((2, StoryletScopes.Ship, (string?)null), (ship.Stakes, ship.Scope, ship.Name));
        Assert.Equal([new FlagRange("flag_a", 1, 1)], ship.Requires);
        Assert.Equal(["option_a", "option_b"], ship.Options.Select(o => o.Id));
        Assert.Equal("STORYLET_01_OPTION_A_LABEL", ship.Options[0].Label);
        Assert.Equal([new StoryletEffect(Flag: new FlagChange("flag_a", 0, true, "")), new StoryletEffect(Loyalty: -1, LoyaltyOn: "crew")], ship.Options[0].Effects);
        StoryletDef crew = pack.Storylets[1];
        Assert.Equal(StoryletScopes.Crew, crew.Scope);
        Assert.Equal([new FlagRange("flag_b", 1, null), new FlagRange("flag_c", 0, 0)], crew.Requires);
        Assert.Equal(
            [new StoryletEffect(Flag: new FlagChange("flag_b", -1, false, "")), new StoryletEffect(Flag: new FlagChange("flag_c", 1, true, ""))],
            crew.Options[0].Effects);
        Assert.Equal([new StoryletEffect(Flag: new FlagChange("flag_c", 2, true, "")), new StoryletEffect(Loyalty: -2, LoyaltyOn: "subject")], crew.Options[1].Effects);
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
    public void A_scope_other_than_ship_or_crew_is_refused(string from, string to)
    {
        AssertRefused(Storylet.Replace(from, to), "STORYLET_X: A storylet needs scope: ship or crew.");
    }

    [Theory]
    [InlineData("requires: []")]
    [InlineData("requires: [{ flag: flag_a }, { flag: flag_b }, { flag: flag_c }, { flag: flag_d }, { flag: flag_e }]")]
    public void No_requirement_or_more_than_four_is_refused(string requires)
    {
        string storylet = Storylet.Replace("requires:\n    - { flag: flag_a, min: 1, max: 1 }", requires);

        AssertRefused(storylet, "STORYLET_X: A storylet needs one to four requirements.");
    }

    [Theory]
    [InlineData("{ flag: Flag_A, min: 1 }", "STORYLET_X: Flag id 'Flag_A' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("{ min: 1 }", "STORYLET_X: Flag id '' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("{ flag: flag_a, over: 1 }", "STORYLET_X: A requirement takes flag, min, and max; 'over' is not one of them.")]
    [InlineData("{ flag: flag_a, min: one }", "STORYLET_X: A requirement's min and max must be whole numbers of 0 or more.")]
    [InlineData("{ flag: flag_a, min: -1 }", "STORYLET_X: A requirement's min and max must be whole numbers of 0 or more.")]
    [InlineData("{ flag: flag_a, min: 2, max: 1 }", "STORYLET_X: The requirement on flag_a has max 1 below min 2.")]
    [InlineData("{ flag: flag_a, max: 1, min: 3 }", "STORYLET_X: The requirement on flag_a has max 1 below min 3.")]
    public void A_requirement_that_is_not_a_flag_and_a_range_is_refused(string requirement, string message)
    {
        AssertRefused(Storylet.Replace("{ flag: flag_a, min: 1, max: 1 }", requirement), message);
    }

    [Fact]
    public void A_requirement_with_only_a_max_starts_at_zero_and_one_with_neither_at_one()
    {
        string storylet = Storylet.Replace("{ flag: flag_a, min: 1, max: 1 }", "{ flag: flag_a, max: 1 }\n    - { flag: flag_b }");

        // Each option must still move a requirement out of range: option_a raises flag_a past its max, option_b clears flag_b.
        storylet = storylet.Replace("{ flag: flag_a, set: 2 }", "{ flag: flag_b, set: 0 }").Replace("{ flag: flag_a, set: 0 }", "{ flag: flag_a, set: 2 }");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
        Assert.Equal([new FlagRange("flag_a", 0, 1), new FlagRange("flag_b", 1, null)], result.Pack!.Storylets.Single().Requires);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Fewer_than_two_or_more_than_four_options_are_refused(int count)
    {
        string options = string.Concat(Enumerable.Range(1, count).Select(n => $"    - {{ id: option_{n}, label: L, effects: [{{ flag: flag_a, set: 0 }}] }}\n"));
        string storylet = Storylet[..Storylet.IndexOf("  options:", StringComparison.Ordinal)] + "  options:\n" + options;

        AssertRefused(storylet, "STORYLET_X: A storylet needs two to four options.");
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
    [InlineData("{ flag: flag_b, add: 1, injuryRoll: light }", "injuryRoll")]
    public void An_effect_other_than_loyalty_or_a_flag_is_refused_by_name(string effect, string key)
    {
        string storylet = Storylet.Replace("effects: [{ flag: flag_a, set: 2 }]", $"effects: [{{ flag: flag_a, set: 2 }}, {effect}]");

        AssertRefused(storylet, $"STORYLET_X: A storylet's effect can change loyalty or a flag; '{key}' is not one of them.");
    }

    [Theory]
    [InlineData("{ loyalty: -1 }", "STORYLET_X: A loyalty change needs on: subject or crew.")]
    [InlineData("{ loyalty: -1, on: ship }", "STORYLET_X: A loyalty change needs on: subject or crew.")]
    [InlineData("{ loyalty: lots, on: crew }", "STORYLET_X: A loyalty change must be a whole number.")]
    [InlineData("{ flag: flag_b, add: 1, on: crew }", "STORYLET_X: A storylet's flag change takes on: ship or subject, or no on.")]
    [InlineData("{ flag: flag_b, add: 1, set: 1 }", "STORYLET_X: A flag change needs add or set, not both.")]
    [InlineData("{ flag: flag_b }", "STORYLET_X: A flag change needs add or set, not both.")]
    [InlineData("{ flag: flag_b, set: -1 }", "STORYLET_X: A flag's set must be 0 or more.")]
    [InlineData("{ flag: Flag_B, add: 1 }", "STORYLET_X: Flag id 'Flag_B' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.")]
    [InlineData("{ flag: flag_b, add: 1, loyalty: 1 }", "STORYLET_X: An effect changes loyalty or a flag, not both.")]
    public void A_malformed_loyalty_or_flag_change_is_refused(string effect, string message)
    {
        string storylet = Storylet.Replace("effects: [{ flag: flag_a, set: 2 }]", $"effects: [{{ flag: flag_a, set: 2 }}, {effect}]");

        AssertRefused(storylet, message);
    }

    [Theory]
    [InlineData("{ loyalty: -1, on: subject }")]
    [InlineData("{ flag: flag_b, add: 1, on: subject }")]
    public void A_storylet_for_the_ship_cannot_change_a_subject(string effect)
    {
        string storylet = Storylet.Replace("scope: crew", "scope: ship").Replace("effects: [{ flag: flag_a, set: 2 }]", $"effects: [{{ flag: flag_a, set: 2 }}, {effect}]");

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
    [InlineData("{ flag: flag_a, set: 1 }")]
    [InlineData("{ flag: flag_a, add: 0 }")]
    [InlineData("{ flag: flag_b, set: 3 }")]
    [InlineData("{ loyalty: -1, on: crew }")]
    [InlineData("{ flag: flag_a, set: 0, on: ship }")]
    public void An_option_that_leaves_every_requirement_true_is_refused(string effect)
    {
        AssertRefused(
            Storylet.Replace("effects: [{ flag: flag_a, set: 2 }]", $"effects: [{effect}]"),
            "STORYLET_X: Option 'option_b' leaves every requirement true, so this storylet would come back every cycle.");
    }

    [Fact]
    public void An_option_that_moves_one_requirement_out_of_range_is_enough()
    {
        string storylet = Storylet.Replace("{ flag: flag_a, min: 1, max: 1 }", "{ flag: flag_a, min: 1, max: 1 }\n    - { flag: flag_b, max: 0 }")
            .Replace("effects: [{ flag: flag_a, set: 2 }]", "effects: [{ flag: flag_b, add: 1, on: subject }]");

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
}

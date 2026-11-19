using Sim.Content;
using Sim.Core.Content;

namespace Sim.Tests;

public class StoryletContentTests
{
    private const string Storylet = """
        - id: STORYLET_X
          stakes: 2
          cast:
            subject: "subject.trait.trait_a == 1"
          options:
            - id: option_a
              label: LABEL_A
              effects: [{ trait: trait_a, set: 0 }]
            - id: option_b
              label: LABEL_B
              effects: [{ trait: trait_a, set: 2 }]

        """;

    private const string Cast = "  cast:\n    subject: \"subject.trait.trait_a == 1\"\n";

    // The same storylet for the ship: no cast, and requires on the ship.
    private static readonly string ShipStorylet = Storylet.Replace(Cast, "  requires: \"ship.trait.trait_a == 1\"\n");

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

    private static string WithCast(string cast)
    {
        return Storylet.Replace(Cast, cast);
    }

    [Fact]
    public void The_fixture_storylets_load()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Equal(["STORYLET_01", "STORYLET_02", "STORYLET_03"], pack.Storylets.Select(s => s.Id));
        Assert.Equal((2, 100), (pack.StoryletsPerCycle, pack.CastTries));
        StoryletDef ship = pack.Storylets[0];
        Assert.Equal((2, 0, "ship.trait.trait_a == 1", (string?)null), (ship.Stakes, ship.Cast.Count, ship.Requires?.Source, ship.Name));
        Assert.Equal([new TraitRead("trait_a", "ship", null)], ship.Reads);
        Assert.Equal(["option_a", "option_b"], ship.Options.Select(o => o.Id));
        Assert.Equal("STORYLET_01_OPTION_A_LABEL", ship.Options[0].Label);
        Assert.Equal([new StoryletEffect(Trait: new TraitChange("trait_a", 0, true, "")), new StoryletEffect(Loyalty: -1, LoyaltyOn: "crew")], ship.Options[0].Effects);
        StoryletDef crew = pack.Storylets[1];
        Assert.Equal([new CastSlot("subject", new Expr("subject.trait.trait_b >= 1 && subject.trait.trait_c == 0"))], crew.Cast);
        Assert.Null(crew.Requires);
        Assert.Equal([new TraitRead("trait_b", "subject", null), new TraitRead("trait_c", "subject", null)], crew.Reads);
        Assert.Equal([new TraitRead("trait_b", "subject", null)], crew.Needs);
        Assert.Equal(
            [
                new StoryletEffect(Trait: new TraitChange("trait_b", -1, false, "")),
                new StoryletEffect(Trait: new TraitChange("trait_c", 1, true, "")),
                new StoryletEffect(Trait: new TraitChange("trait_e", 1, true, "")),
            ],
            crew.Options[0].Effects);
        Assert.Equal([new StoryletEffect(Trait: new TraitChange("trait_c", 2, true, "")), new StoryletEffect(Loyalty: -2, LoyaltyOn: "subject")], crew.Options[1].Effects);
        StoryletDef pair = pack.Storylets[2];
        Assert.Equal(["subject", "target"], pair.Slots);
        Assert.Equal([new TraitRead("trait_g", "subject", "target")], pair.Reads);
        Assert.Equal(new StoryletEffect(Trait: new TraitChange("trait_g", 0, true, "", "target")), pair.Options[0].Effects[0]);
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

    // 1e part four, design 4.2: casting with up to three named slots, and requires as one expression.
    [Fact]
    public void Three_slots_load_in_reading_order_with_requires_and_the_traits_they_read()
    {
        string storylet = WithCast("  cast:\n    subject: \"subject.trait.trait_a == 1\"\n    target: \"target.fit\"\n    witness: \"witness.trait.trait_g.toward(target) >= 1\"\n"
            + "  requires: \"subject.morale < target.morale && ship.trait.trait_c == 0\"\n");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
        StoryletDef def = result.Pack!.Storylets.Single();
        Assert.Equal(["subject", "target", "witness"], def.Slots);
        Assert.Equal("subject.morale < target.morale && ship.trait.trait_c == 0", def.Requires?.Source);
        Assert.Equal([new TraitRead("trait_a", "subject", null), new TraitRead("trait_g", "witness", "target"), new TraitRead("trait_c", "ship", null)], def.Reads);
        Assert.Equal([new TraitRead("trait_a", "subject", null), new TraitRead("trait_g", "witness", "target")], def.Needs);
    }

    [Theory]
    [InlineData("  scope: crew\n")]
    [InlineData("  scope: ship\n")]
    public void Scope_is_refused_with_a_pointer_to_cast(string scope)
    {
        AssertRefused(Storylet.Replace("  stakes: 2\n", "  stakes: 2\n" + scope), "STORYLET_X: scope was replaced by cast in part four; a storylet for crew casts subject.");
    }

    [Fact]
    public void The_list_form_of_requires_is_refused()
    {
        AssertRefused(
            Storylet.Replace(Cast, Cast + "  requires:\n    - { trait: trait_a, min: 1 }\n"),
            "STORYLET_X: requires is one expression since part four, not a list; write it as \"subject.trait.shaken >= 1 && subject.morale <= 25\".");
    }

    [Theory]
    [InlineData("ship")]
    [InlineData("crew")]
    [InlineData("captain")]
    [InlineData("cycle")]
    [InlineData("best")]
    [InlineData("handler")]
    public void A_reserved_slot_name_is_refused(string name)
    {
        AssertRefused(
            WithCast($"  cast:\n    {name}: \"true\"\n    subject: \"subject.trait.trait_a == 1\"\n"),
            $"STORYLET_X: '{name}' cannot name a slot; ship, crew, captain, cycle, best, or handler each mean something already.");
    }

    [Fact]
    public void A_slot_name_that_is_not_lower_case_is_refused()
    {
        AssertRefused(WithCast("  cast:\n    Subject: \"Subject.trait.trait_a == 1\"\n"),
            "STORYLET_X: Slot name 'Subject' must be lower case: a letter, then letters, digits, and underscores, at most 40 in all.");
    }

    [Fact]
    public void More_than_three_slots_are_refused()
    {
        AssertRefused(WithCast("  cast:\n    a: \"a.trait.trait_a == 1\"\n    b: \"b.fit\"\n    c: \"c.fit\"\n    d: \"d.fit\"\n"), "STORYLET_X: A cast is at most three slots.");
    }

    [Fact]
    public void A_slot_cannot_read_a_later_one()
    {
        AssertRefused(
            WithCast("  cast:\n    subject: \"subject.trait.trait_a == 1 && subject != target\"\n    target: \"target.fit\"\n"),
            "STORYLET_X: cast.subject, at character 42: target is not a slot this expression can read; it reads subject, ship, and cycle.");
    }

    [Fact]
    public void A_bad_trait_is_refused_with_its_key_and_position()
    {
        AssertRefused(WithCast("  cast:\n    subject: \"subject.trait.nosuch >= 1\"\n"), "STORYLET_X: cast.subject, at character 15: No trait named nosuch.");
        AssertRefused(Storylet.Replace(Cast, Cast + "  requires: \"subject.trait.trait_a == 1 && subject.rel > 0\"\n"),
            "STORYLET_X: requires, at character 39: rel() waits for relationships (Phase 4).");
    }

    [Fact]
    public void A_storylet_for_the_ship_cannot_read_a_slot()
    {
        AssertRefused(ShipStorylet.Replace("  requires: \"ship.trait.trait_a == 1\"\n", "  requires: \"ship.trait.trait_a == 1 && subject.morale < 25\"\n"),
            "STORYLET_X: requires, at character 28: subject is not a slot this expression can read; it reads ship and cycle.");
    }

    [Fact]
    public void Toward_may_not_name_the_slot_the_trait_is_on()
    {
        string pair = WithCast("  cast:\n    subject: \"subject.aboard\"\n    target: \"subject.trait.trait_g.toward(target) >= 1\"\n")
            .Replace("effects: [{ trait: trait_a, set: 0 }]", "effects: [{ trait: trait_g, set: 0, toward: target }]");
        Assert.Empty(LoadWith(pair.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_g, set: 0, toward: target }]")).Errors);

        AssertRefused(
            pair.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_g, set: 0, on: target, toward: target }]"),
            "STORYLET_X: toward: target names the slot the trait is on; a pair trait is held toward someone else.");
        AssertRefused(
            pair.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_g, set: 0, toward: subject }]"),
            "STORYLET_X: toward: subject names the slot the trait is on; a pair trait is held toward someone else.");
        AssertRefused(
            pair.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_g, set: 0, toward: witness }]"),
            "STORYLET_X: toward: witness names no slot; this storylet casts subject or target.");
    }

    [Fact]
    public void On_and_speakers_take_any_slot_name()
    {
        string storylet = WithCast("  cast:\n    boss: \"boss.trait.trait_a == 1\"\n    rookie: \"rookie.fit\"\n")
            .Replace("  options:\n", "  text: |-\n    rookie: #boss# said so.\n    boss: Did I.\n  options:\n")
            .Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_a, set: 2, on: boss }, { morale: 1, on: rookie }]");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
        Assert.Equal(new StoryletEffect(Morale: 1, MoraleOn: "rookie"), result.Pack!.Storylets.Single().Options[1].Effects[1]);
    }

    // Review focus 4: an option's requires is parsed and checked now (read in play from Task 6), and one option is always free.
    [Fact]
    public void An_option_requires_is_checked_and_one_option_must_have_none()
    {
        string guarded = Storylet.Replace("      label: LABEL_B\n", "      label: LABEL_B\n      requires: \"subject.loyalty >= 60\"\n");
        ContentLoadResult result = LoadWith(guarded);
        Assert.Empty(result.Errors);
        Assert.Equal("subject.loyalty >= 60", result.Pack!.Storylets.Single().Options[1].Requires?.Source);

        AssertRefused(Storylet.Replace("      label: LABEL_B\n", "      label: LABEL_B\n      requires: \"subject.loyalty >= target\"\n"),
            "STORYLET_X: options.option_b.requires, at character 20: target is not a slot this expression can read; it reads subject, ship, and cycle.");
        AssertRefused(guarded.Replace("      label: LABEL_A\n", "      label: LABEL_A\n      requires: \"subject.fit\"\n"),
            "STORYLET_X: A storylet needs an option with no requires, so it can always be answered.");
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
    public void Expressions_that_no_trait_values_satisfy_are_refused()
    {
        AssertRefused(
            Storylet.Replace(Cast, Cast + "  requires: \"subject.trait.trait_a >= 2\"\n"),
            "STORYLET_X: No values of the traits it reads let every expression hold, so it can never arrive.");
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
    [InlineData("{ loyalty: -1 }", "STORYLET_X: A loyalty change needs on: crew or a slot; this storylet casts subject.")]
    [InlineData("{ loyalty: -1, on: ship }", "STORYLET_X: A loyalty change needs on: crew or a slot; this storylet casts subject.")]
    [InlineData("{ loyalty: -1, on: target }", "STORYLET_X: on: target names no slot; this storylet casts subject.")]
    [InlineData("{ loyalty: lots, on: crew }", "STORYLET_X: A loyalty change must be a whole number.")]
    [InlineData("{ trait: trait_b, add: 1, on: crew }", "STORYLET_X: A storylet's trait change takes on: ship or a slot, or no on; this storylet casts subject.")]
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
    public void A_storylet_for_the_ship_cannot_change_a_slot(string effect)
    {
        string storylet = ShipStorylet.Replace("effects: [{ trait: trait_a, set: 2 }]", $"effects: [{{ trait: trait_a, set: 2 }}, {effect}]");

        AssertRefused(storylet, "STORYLET_X: on: subject names no slot; this storylet casts no one.");
    }

    [Theory]
    [InlineData("  text: \"subject: Hello.\"\n", "STORYLET_X: text cannot be used: speaker 'subject' is not in a storylet beat, which has no one.")]
    [InlineData("  text: \"#subject# waited.\"\n", "STORYLET_X: text cannot be used: #subject# names 'subject', which a storylet beat does not have.")]
    public void A_storylet_for_the_ship_cannot_name_a_subject_in_its_text(string text, string message)
    {
        AssertRefused(ShipStorylet.Replace("  options:\n", text + "  options:\n"), message);
    }

    [Fact]
    public void A_storylet_for_crew_may_name_its_subject_and_the_best_at_a_skill()
    {
        string storylet = Storylet.Replace("  options:\n", "  text: |-\n    subject: #subject.they# asked.\n    best SKILL_A: Noted.\n    = #subject# asked about #storylet#.\n  options:\n")
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
        AssertRefused(Storylet.Replace("  options:\n", text + "  options:\n"), message);
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
    public void An_option_that_leaves_every_expression_true_is_refused(string effect)
    {
        AssertRefused(
            Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", $"effects: [{effect}]"),
            "STORYLET_X: Option 'option_b' leaves every expression true, so this storylet would come back every cycle.");
    }

    [Fact]
    public void An_option_that_makes_one_trait_clause_false_is_enough()
    {
        string storylet = WithCast("  cast:\n    subject: \"subject.trait.trait_a == 1 && subject.trait.trait_b == 0\"\n")
            .Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_b, add: 1, on: subject }]");

        Assert.Empty(LoadWith(storylet).Errors);
    }

    // Design 4.6: a clause on a stat or the history may still hold, so only a trait the option moves can end the storylet.
    [Fact]
    public void A_clause_on_a_stat_or_the_history_counts_as_may_still_hold()
    {
        string storylet = WithCast("  cast:\n    subject: \"subject.trait.trait_a == 1 && subject.morale < 30\"\n");

        Assert.Empty(LoadWith(storylet).Errors);
        AssertRefused(
            storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ morale: 40, on: subject }]"),
            "STORYLET_X: Option 'option_b' leaves every expression true, so this storylet would come back every cycle.");
        AssertRefused(
            WithCast("  cast:\n    subject: \"subject.trait.trait_a == 1 || subject.history.last('hurt')\"\n"),
            "STORYLET_X: Option 'option_a' leaves every expression true, so this storylet would come back every cycle.");
    }

    [Theory]
    [InlineData("storylets:\n  perCycle: 2", "", "storylets.perCycle is missing.")]
    [InlineData("perCycle: 2", "perCycle: -1", "storylets.perCycle must be a whole number of 0 or more, not '-1'.")]
    [InlineData("castTries: 100", "castTries: 0", "storylets.castTries must be a whole number of 1 or more, not '0'.")]
    public void The_storylets_per_cycle_and_cast_tries_are_required_tuning_values(string from, string to, string message)
    {
        string tuning = TestContent.TuningText.Replace(from, to);

        ContentLoadResult result = LoadWith(Storylet, tuning);

        Assert.Contains(result.Errors, e => e.File == "tuning/missions.yaml" && e.Message == message);
    }

    // 1e part four, Task 5 (design 4.5): the salience weights.
    [Theory]
    [InlineData("  stakes: 10", "  stakes: x", "salience.stakes must be a whole number of 0 or more, not 'x'.")]
    [InlineData("  specificity: 1", "", "salience.specificity is missing.")]
    [InlineData("  repeat: 3", "  repeat: -1", "salience.repeat must be a whole number of 0 or more, not '-1'.")]
    [InlineData("  window: 20", "  window: -2", "salience.window must be a whole number of 0 or more, not '-2'.")]
    public void The_salience_weights_are_required_tuning_values(string from, string to, string message)
    {
        string tuning = TestContent.TuningText.Replace(from, to);

        ContentLoadResult result = LoadWith(Storylet, tuning);

        Assert.Contains(result.Errors, e => e.File == "tuning/missions.yaml" && e.Message == message);
    }

    [Fact]
    public void The_fixtures_salience_weights_load()
    {
        Assert.Equal(new SalienceTuning(10, 1, 3, 20), TestContent.Pack.Salience);
    }

    // Design 4.6: cooldowns, in cycles. Either part may be left out, and both default to 0.
    [Fact]
    public void A_cooldown_loads_and_both_parts_default_to_0()
    {
        static Cooldown Of(string cooldown)
        {
            return LoadWith(Storylet.Replace("  options:", $"  cooldown: {cooldown}\n  options:")).Pack!.Storylets.Single().Cooldown;
        }

        Assert.Equal(new Cooldown(8, 2), Of("{ cast: 8, global: 2 }"));
        Assert.Equal(new Cooldown(0, 3), Of("{ global: 3 }"));
        Assert.Equal(Cooldown.None, LoadWith(Storylet).Pack!.Storylets.Single().Cooldown);
    }

    [Theory]
    [InlineData("{ cast: -1 }")]
    [InlineData("{ cast: 2, global: -3 }")]
    public void A_negative_cooldown_is_refused(string cooldown)
    {
        AssertRefused(Storylet.Replace("  options:", $"  cooldown: {cooldown}\n  options:"), "STORYLET_X: A cooldown's cast and global are whole numbers of cycles, 0 or more.");
    }

    // Design 4.6: with cooldown.cast above 0 a storylet may recur by design, so an answer that leaves it holding is allowed; a
    // global cooldown alone does not lift the rule.
    [Fact]
    public void A_recurring_storylet_with_cast_cooldown_passes_the_hidden_memory_rule()
    {
        string recurring = Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ loyalty: -1, on: crew }]");

        AssertRefused(
            recurring.Replace("  options:", "  cooldown: { global: 4 }\n  options:"),
            "STORYLET_X: Option 'option_b' leaves every expression true, so this storylet would come back every cycle.");
        Assert.Empty(LoadWith(recurring.Replace("  options:", "  cooldown: { cast: 4 }\n  options:")).Errors);
    }

    // Design 4.2: "must read a trait" may be met by the cooldown rule instead.
    [Fact]
    public void A_storylet_with_a_cast_cooldown_need_not_read_a_trait()
    {
        string cast = "  cast:\n    subject: \"subject.morale <= 25\"\n";

        AssertRefused(WithCast(cast), "STORYLET_X: A storylet's cast and requires must read at least one trait, so its answers can stop it returning.");
        Assert.Empty(LoadWith(WithCast(cast + "  cooldown: { cast: 6 }\n")).Errors);
    }

    // 1e part two's stats and last cycle, read in expressions (design 4.3).
    [Fact]
    public void Stats_and_the_history_load_beside_a_trait()
    {
        string storylet = WithCast("  cast:\n    subject: \"subject.trait.trait_a == 1 && subject.morale <= 25 && subject.loyalty >= 60 && !subject.history.last('hurt')\"\n");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void A_morale_change_loads_on_a_slot_or_the_crew()
    {
        string storylet = Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ trait: trait_a, set: 2 }, { morale: 3, on: subject }, { morale: -1, on: crew }]");

        ContentLoadResult result = LoadWith(storylet);

        Assert.Empty(result.Errors);
        Assert.Equal(
            [new StoryletEffect(Morale: 3, MoraleOn: "subject"), new StoryletEffect(Morale: -1, MoraleOn: "crew")],
            result.Pack!.Storylets.Single().Options[1].Effects.Skip(1));
    }

    [Theory]
    [InlineData("  cast:\n    subject: \"subject.morale <= 25\"\n")]
    [InlineData("  cast:\n    subject: \"subject.fit\"\n  requires: \"subject.history.count('hurt', 3) >= 2\"\n")]
    public void A_storylet_that_reads_no_trait_is_refused(string cast)
    {
        AssertRefused(WithCast(cast), "STORYLET_X: A storylet's cast and requires must read at least one trait, so its answers can stop it returning.");
    }

    [Theory]
    [InlineData("{ morale: 1 }", "STORYLET_X: A morale change needs on: crew or a slot; this storylet casts subject.")]
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
        AssertRefused(Storylet.Replace("effects: [{ trait: trait_a, set: 2 }]", "effects: [{ flag: trait_a, set: 2 }]"), "STORYLET_X: 'flag' is now 'trait' (GDD section 15, item 40).");

        Dictionary<string, string> files = TestContent.RepoFiles();
        files["encounters/base.yaml"] = files["encounters/base.yaml"].Replace("{ trait: trait_a, add: 1, on: ship }", "{ flag: trait_a, add: 1, on: ship }");
        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Null(result.Pack);
        Assert.Contains(result.Errors, e => e.File == "encounters/base.yaml" && e.Message.EndsWith("'flag' is now 'trait' (GDD section 15, item 40).", StringComparison.Ordinal));
    }

    // Both packs load with every storylet and vignette migrated (review focus 2).
    [Fact]
    public void The_games_pack_loads()
    {
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(TestContent.Root, "content"));

        Assert.Empty(result.Errors);
        Assert.Equal(["STORYLET_01", "STORYLET_02", "STORYLET_03", "STORYLET_04"], result.Pack!.Storylets.Select(s => s.Id));
        Assert.Equal(["rested", "hurt", "lost_crewmate", "sent", null, null], result.Pack.Vignettes.Select(v => v.Fact));
    }
}

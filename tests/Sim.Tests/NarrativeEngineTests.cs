using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Narrative;

namespace Sim.Tests;

// Salience selection and expansion on hand-built situations (GDD section 6; slice 1d design, decisions 5 and 12).
public class NarrativeEngineTests
{
    private static readonly PronounSet She = new("she", "she", "her", "her", "hers", "herself", false);

    private static Situation Check(string tier = "failure", int caution = 1, bool helper = false, PronounSet? pronouns = null)
    {
        var facts = new Dictionary<string, FactValue>
        {
            ["encounter"] = FactValue.Of("ENCOUNTER_01"),
            ["encounterTags"] = FactValue.Of(["field", "hazard"]),
            ["tier"] = FactValue.Of(tier),
            ["margin"] = FactValue.Of(-5),
            ["supervised"] = FactValue.Of(false),
            ["actor.caution"] = FactValue.Of(caution),
        };
        var people = new Dictionary<string, Person> { ["actor"] = new("CREW_01", "NAME_A", pronouns ?? PronounSet.They) };
        if (helper)
        {
            people["helper"] = new Person("CREW_02", "NAME_B", She);
            facts["helper.caution"] = FactValue.Of(0);
        }

        return new Situation("check", facts, people, new Dictionary<string, string> { ["encounter"] = "PLACE_A", ["skill"] = "SKILL_A_NAME" });
    }

    private static LineDef Line(string id, params CriterionDef[] when) => new(id, "check", when, $"{id}.");

    private static CriterionDef Is(string fact, params string[] values) => new(fact, values);

    private static readonly IReadOnlySet<string> NoneUsed = new HashSet<string>();

    private static readonly Dictionary<string, SymbolDef> NoSymbols = [];

    [Fact]
    public void The_line_with_the_most_criteria_that_hold_wins()
    {
        LineDef[] lines = [Line("LINE_GENERIC"), Line("LINE_TIER", Is("tier", "failure")), Line("LINE_TIER_TAG", Is("tier", "failure"), Is("encounterTags", "hazard"))];

        Assert.Equal("LINE_TIER_TAG", LineSelector.Select(lines, Check(), NoneUsed, "k")!.Id);
        Assert.Equal("LINE_GENERIC", LineSelector.Select(lines, Check(tier: "success"), NoneUsed, "k")!.Id);
    }

    [Fact]
    public void A_line_of_another_beat_never_wins()
    {
        LineDef[] lines = [new("LINE_STAGE", "stage", [], "x")];

        Assert.Null(LineSelector.Select(lines, Check(), NoneUsed, "k"));
    }

    [Theory]
    [InlineData("tier", new[] { "partial", "failure" }, true)]
    [InlineData("tier", new[] { "partial" }, false)]
    [InlineData("encounterTags", new[] { "social", "hazard" }, true)]
    [InlineData("encounterTags", new[] { "social" }, false)]
    [InlineData("supervised", new[] { "false" }, true)]
    [InlineData("margin", new[] { "-5" }, true)]
    [InlineData("helper.caution", new[] { "0" }, false)]
    public void A_criterion_holds_by_any_of_its_values_and_never_on_an_absent_fact(string fact, string[] values, bool holds)
    {
        Assert.Equal(holds, LineSelector.Holds(new CriterionDef(fact, values), Check()));
    }

    [Theory]
    [InlineData(1, null, true)]
    [InlineData(2, null, false)]
    [InlineData(null, 1, true)]
    [InlineData(null, 0, false)]
    [InlineData(0, 2, true)]
    public void A_range_holds_between_its_ends_either_of_which_may_be_open(int? min, int? max, bool holds)
    {
        Assert.Equal(holds, LineSelector.Holds(new CriterionDef("actor.caution", [], min, max), Check(caution: 1)));
    }

    [Fact]
    public void A_range_on_a_fact_that_is_not_a_number_never_holds()
    {
        Assert.False(LineSelector.Holds(new CriterionDef("tier", [], 0, 9), Check()));
    }

    [Fact]
    public void A_tie_is_broken_by_the_seed_key_and_the_same_key_always_gives_the_same_line()
    {
        LineDef[] lines = Enumerable.Range(1, 6).Select(i => Line($"LINE_{i}", Is("tier", "failure"))).ToArray();

        HashSet<string> winners = Enumerable.Range(0, 40).Select(k => LineSelector.Select(lines, Check(), NoneUsed, $"key{k}")!.Id).ToHashSet();
        Assert.True(winners.Count > 1);
        Assert.All(Enumerable.Range(0, 5), _ => Assert.Equal(LineSelector.Select(lines, Check(), NoneUsed, "key7")!.Id, LineSelector.Select(lines, Check(), NoneUsed, "key7")!.Id));
    }

    [Fact]
    public void A_used_line_loses_to_an_equally_specific_unused_one_but_not_to_a_less_specific_one()
    {
        LineDef[] lines = [Line("LINE_A", Is("tier", "failure")), Line("LINE_B", Is("tier", "failure")), Line("LINE_GENERIC")];

        Assert.Equal("LINE_B", LineSelector.Select(lines, Check(), new HashSet<string> { "LINE_A" }, "k")!.Id);
        Assert.Equal("LINE_A", LineSelector.Select(lines, Check(), new HashSet<string> { "LINE_B" }, "k")!.Id);
        Assert.Contains(LineSelector.Select(lines, Check(), new HashSet<string> { "LINE_A", "LINE_B" }, "k")!.Id, new[] { "LINE_A", "LINE_B" });
    }

    [Fact]
    public void Candidates_are_ordered_most_specific_first_then_by_id()
    {
        LineDef[] lines = [Line("LINE_B"), Line("LINE_C", Is("tier", "failure")), Line("LINE_A")];

        Assert.Equal(["LINE_C", "LINE_A", "LINE_B"], LineSelector.Candidates(lines, Check()).Select(l => l.Id));
    }

    [Fact]
    public void Names_stay_name_parts_and_text_runs_merge()
    {
        IReadOnlyList<TextPart> parts = Expander.Expand("#actor# checks #skill# at #encounter#.", Check(), NoSymbols, "k");

        Assert.Equal(
            [new(true, "NAME_A"), new(false, " checks "), new(true, "SKILL_A_NAME"), new(false, " at "), new(true, "PLACE_A"), new(false, ".")],
            parts);
    }

    [Theory]
    [InlineData("#actor.they# #actor:checks|check# #actor.their# kit.", "they check their kit.")]
    [InlineData("#helper.they# #helper:checks|check# #helper.their# kit.", "she checks her kit.")]
    [InlineData("#helper.them#, #helper.theirs#, #helper.themself#", "her, hers, herself")]
    [InlineData("#helper.they.capitalize# waits.", "She waits.")]
    [InlineData("A ## B", "A # B")]
    public void Pronouns_verbs_capitals_and_a_literal_hash(string text, string expected)
    {
        Assert.Equal(expected, string.Concat(Expander.Expand(text, Check(helper: true), NoSymbols, "k").Select(p => p.Text)));
    }

    [Fact]
    public void Symbols_expand_with_nesting_and_the_same_key_gives_the_same_words()
    {
        var symbols = new Dictionary<string, SymbolDef>
        {
            ["outer"] = new("outer", ["[#inner#]"]),
            ["inner"] = new("inner", ["WORD_A", "WORD_B", "WORD_C"]),
        };

        string once = string.Concat(Expander.Expand("#outer# #outer#", Check(), symbols, "k").Select(p => p.Text));
        Assert.Matches(@"^\[WORD_[ABC]\] \[WORD_[ABC]\]$", once);
        Assert.Equal(once, string.Concat(Expander.Expand("#outer# #outer#", Check(), symbols, "k").Select(p => p.Text)));
        HashSet<string> seen = Enumerable.Range(0, 30).Select(i => string.Concat(Expander.Expand("#inner#", Check(), symbols, $"k{i}").Select(p => p.Text))).ToHashSet();
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void Symbols_nesting_past_the_cap_are_refused()
    {
        var symbols = new Dictionary<string, SymbolDef> { ["loop"] = new("loop", ["#loop#"]) };

        Assert.Throws<ExpansionException>(() => Expander.Expand("#loop#", Check(), symbols, "k"));
    }

    [Theory]
    [InlineData("#nobody#")]
    [InlineData("#helper.they#")]
    [InlineData("#encounter.they#")]
    [InlineData("#actor.her#")]
    [InlineData("#actor:checks#")]
    [InlineData("an open # mark")]
    public void Text_that_names_nothing_or_breaks_the_grammar_is_refused(string text)
    {
        Assert.Throws<ExpansionException>(() => Expander.Expand(text, Check(), NoSymbols, "k"));
    }
}

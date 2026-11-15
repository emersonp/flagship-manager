using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Missions;

namespace Sim.Tests;

// Loading lines, symbols, and pronoun sets (slice 1d design, section 2), and the beat-to-situation mapping that feeds them.
public class NarrativeContentTests
{
    private static ContentLoadResult LoadWith(string path, string text)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files[path] = text;
        return ContentLoader.Load(files);
    }

    private static string OneLine(string when, string text = "\"#actor# acts.\"", string extra = "") => $"- id: LINE_TEST\n  when: {when}\n  text: {text}\n{extra}";

    [Fact]
    public void The_fixture_pack_loads_its_lines_and_pronoun_sets()
    {
        ContentPack pack = TestContent.Pack;

        Assert.Contains(pack.Lines, l => l.Id == "LINE_CHECK_HELPED" && l.Beat == "check" && l.Owner is null && l.When.Single() is { Fact: "helped", Values: ["true"] });
        Assert.Contains(pack.Lines, l => l.Id == "LINE_CHECK_BAD" && l.When.Single().Values.SequenceEqual(new[] { "failure", "disaster" }));
        Assert.Equal(["she", "he", "they"], pack.Pronouns.Select(p => p.Id));
        Assert.Equal(PronounSet.They with { }, pack.PronounsOf(null) with { });
    }

    [Fact]
    public void A_range_and_a_number_load_as_written()
    {
        ContentLoadResult result = LoadWith("lines/test.yaml", OneLine("{ beat: check, actor.caution: { min: -1, max: 2 }, margin: 4 }"));

        Assert.Empty(result.Errors);
        LineDef line = result.Pack!.Lines.Single(l => l.Id == "LINE_TEST");
        Assert.Equal(new CriterionDef("actor.caution", [], -1, 2), line.When[0] with { Values = [] });
        Assert.Equal(["4"], line.When[1].Values);
        Assert.Equal(3, line.Specificity);
    }

    [Theory]
    [InlineData("{ tier: failure }", "when must name a beat")]
    [InlineData("{ beat: dance }", "unknown beat 'dance'")]
    [InlineData("{ beat: stage, tier: failure }", "a stage beat has no fact 'tier'")]
    [InlineData("{ beat: check, tier: grand }", "tier is one of critical, success")]
    [InlineData("{ beat: check, encounter: ENCOUNTER_99 }", "encounter names 'ENCOUNTER_99', which the pack does not have")]
    [InlineData("{ beat: check, skill: SKILL_Z }", "skill names 'SKILL_Z'")]
    [InlineData("{ beat: check, tier: { min: 1 } }", "tier is not a number, so it cannot take a range")]
    [InlineData("{ beat: check, margin: { min: 3, max: 1 } }", "min 3 is above max 1")]
    [InlineData("{ beat: check, margin: { least: 1 } }", "takes min, max, or both")]
    [InlineData("{ beat: check, margin: lots }", "margin is a number, not 'lots'")]
    [InlineData("{ beat: check, supervised: maybe }", "supervised is true or false")]
    [InlineData("{ beat: check, helper.mood: 1 }", "a check beat has no fact 'helper.mood'")]
    [InlineData("{ beat: stage, actor.caution: 1 }", "a stage beat has no fact 'actor.caution'")]
    public void A_line_whose_criteria_can_never_hold_is_refused(string when, string message)
    {
        ContentLoadResult result = LoadWith("lines/test.yaml", OneLine(when));

        Assert.Contains(result.Errors, e => e.File == "lines/test.yaml" && e.Message.Contains(message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{ beat: stage }", "\"#actor# acts.\"", "a stage beat does not have")]
    [InlineData("{ beat: stage }", "\"#encounter.they# acts.\"", "needs a person")]
    [InlineData("{ beat: check }", "\"#nobody# acts.\"", "names no person, thing, or symbol")]
    [InlineData("{ beat: check }", "\"An open # mark.\"", "never closed")]
    [InlineData("{ beat: check }", "\"#actor:checks# it.\"", "needs two verb forms")]
    [InlineData("{ beat: check }", "\"\"", "text must not be empty")]
    public void A_line_whose_text_names_what_its_beat_lacks_is_refused(string when, string text, string message)
    {
        ContentLoadResult result = LoadWith("lines/test.yaml", OneLine(when, text));

        Assert.Contains(result.Errors, e => e.File == "lines/test.yaml" && e.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void A_line_that_still_names_a_use_is_refused_in_words()
    {
        ContentLoadResult result = LoadWith("lines/test.yaml", OneLine("{ beat: check }", extra: "  use: report\n"));

        Assert.Contains(result.Errors, e => e.Message.Contains("use is no longer read: one line serves both the log and the report", StringComparison.Ordinal));
    }

    private const string Encounter1 = "- id: ENCOUNTER_01\n  tags: [field, hazard]\n  chain: ENCOUNTER_CHAIN\n";

    private static ContentLoadResult LoadEncounter1(string body)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        int start = files["encounters/base.yaml"].IndexOf("- id: ENCOUNTER_01\n", StringComparison.Ordinal);
        int end = files["encounters/base.yaml"].IndexOf("- id: ENCOUNTER_02\n", StringComparison.Ordinal);
        files["encounters/base.yaml"] = files["encounters/base.yaml"][..start] + Encounter1 + body + files["encounters/base.yaml"][end..];
        return ContentLoader.Load(files);
    }

    // The old key must fail loudly, not be dropped: an author's words would vanish otherwise.
    [Fact]
    public void A_decision_text_keyed_notice_is_refused()
    {
        ContentLoadResult result = LoadEncounter1(
            "  approaches:\n    careful: { skill: SKILL_A, difficulty: 5 }\n    fast: { skill: SKILL_C, difficulty: 6 }\n"
            + "  decision:\n    id: DECISION_PROMPT_1\n    stakes: 1\n    bold: [{ momentum: 1 }]\n    safe: []\n    text:\n      notice: \"TEXT\"\n");

        Assert.Contains(result.Errors, e => e.Message.Contains("notice", StringComparison.Ordinal));
    }

    // An encounter's own words become lines whose criteria are where they sit, so they outrank the shared lines for their beats.
    [Fact]
    public void An_encounters_own_text_becomes_lines_for_its_beats()
    {
        ContentLoadResult result = LoadEncounter1(
            "  text:\n    arrive: \"#lead# reached #encounter#.\"\n"
            + "  approaches:\n    careful:\n      skill: SKILL_A\n      difficulty: 5\n      text:\n        failure: \"#actor# slipped, and #actor.they# #actor:knows|know# it.\"\n    fast: { skill: SKILL_C, difficulty: 6 }\n"
            + "  decision:\n    id: DECISION_PROMPT_1\n    stakes: 1\n    bold: [{ momentum: 1 }]\n    safe: []\n    text:\n      call: \"#lead# asked about #decision#.\"\n      initiative: \"#lead# settled #decision#.\"\n      bold: The bold choice held.\n      safe: The safe choice held.\n");

        Assert.Empty(result.Errors);
        List<LineDef> owned = result.Pack!.Lines.Where(l => l.Owner == "ENCOUNTER_01").ToList();
        Assert.Equal(
            ["ENCOUNTER_01.arrive", "ENCOUNTER_01.careful.failure", "ENCOUNTER_01.decision.call", "ENCOUNTER_01.decision.initiative", "ENCOUNTER_01.decision.bold", "ENCOUNTER_01.decision.safe"],
            owned.Select(l => l.Id));
        Assert.Equal(["stage", "check", "call", "initiative", "effects", "effects"], owned.Select(l => l.Beat));
        Assert.Equal(["encounter=ENCOUNTER_01", "approach=careful", "tier=failure"], owned[1].When.Select(c => $"{c.Fact}={c.Values.Single()}"));
        Assert.Equal(["decision=DECISION_PROMPT_1", "chose=bold"], owned[4].When.Select(c => $"{c.Fact}={c.Values.Single()}"));
        Assert.Equal(4, owned[1].Specificity);
    }

    // A line written inside an encounter is an ordinary line for any beat, with the encounter as one more criterion.
    [Fact]
    public void A_line_written_inside_an_encounter_holds_only_there_and_outranks_the_shared_one()
    {
        ContentLoadResult result = LoadEncounter1(
            "  approaches:\n    careful: { skill: SKILL_A, difficulty: 5 }\n    fast: { skill: SKILL_C, difficulty: 6 }\n"
            + "  lines:\n    - when: { beat: injury, outcome: dead }\n      text: \"#crew# was run through at #encounter#.\"\n");

        Assert.Empty(result.Errors);
        LineDef line = result.Pack!.Lines.Single(l => l.Owner == "ENCOUNTER_01");
        Assert.Equal(("ENCOUNTER_01.lines[1]", "injury"), (line.Id, line.Beat));
        Assert.Equal(["encounter=ENCOUNTER_01", "outcome=dead"], line.When.Select(c => $"{c.Fact}={c.Values.Single()}"));
        Assert.True(line.Specificity > result.Pack.Lines.Single(l => l.Id == "LINE_INJURY_DEAD").Specificity);
    }

    [Theory]
    [InlineData("    - when: { beat: injury, encounter: ENCOUNTER_02 }\n      text: x\n", "ENCOUNTER_01: lines[1]: encounter is already ENCOUNTER_01, because the line is written inside it.")]
    [InlineData("    - when: { beat: injury, tier: failure }\n      text: x\n", "ENCOUNTER_01: lines[1]: a injury beat has no fact 'tier'.")]
    [InlineData("    - id: LINE_X\n      when: { beat: injury }\n      text: x\n", "ENCOUNTER_01: lines[1]: a line written inside an item takes no id or name.")]
    public void A_line_inside_an_encounter_that_can_never_work_is_refused(string lines, string message)
    {
        ContentLoadResult result = LoadEncounter1("  approaches:\n    careful: { skill: SKILL_A, difficulty: 5 }\n    fast: { skill: SKILL_C, difficulty: 6 }\n  lines:\n" + lines);

        Assert.Contains(result.Errors, e => e.File == "encounters/base.yaml" && e.Message.StartsWith(message, StringComparison.Ordinal));
    }

    [Fact]
    public void An_objectives_own_text_becomes_a_line_for_its_result()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["objectives/base.yaml"] = files["objectives/base.yaml"].Replace("- id: OBJECTIVE_TEMPLATE_1\n", "- id: OBJECTIVE_TEMPLATE_1\n  text:\n    loss: \"#lead# brought no one home from #objective#.\"\n", StringComparison.Ordinal);

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Empty(result.Errors);
        LineDef line = result.Pack!.Lines.Single(l => l.Owner == "OBJECTIVE_TEMPLATE_1");
        Assert.Equal(("OBJECTIVE_TEMPLATE_1.loss", "result"), (line.Id, line.Beat));
        Assert.Equal(["objective=OBJECTIVE_TEMPLATE_1", "outcome=loss"], line.When.Select(c => $"{c.Fact}={c.Values.Single()}"));
    }

    [Theory]
    [InlineData("  text:\n    leave: x\n  approaches:\n    careful: { skill: SKILL_A, difficulty: 5 }\n    fast: { skill: SKILL_C, difficulty: 6 }\n", "ENCOUNTER_01: text.leave is not a text this can have; it must be one of arrive.")]
    [InlineData("  text:\n    arrive: \"#actor# arrived.\"\n  approaches:\n    careful: { skill: SKILL_A, difficulty: 5 }\n    fast: { skill: SKILL_C, difficulty: 6 }\n", "ENCOUNTER_01: text.arrive cannot be used: #actor# names 'actor', which a stage beat does not have.")]
    [InlineData("  approaches:\n    careful:\n      skill: SKILL_A\n      difficulty: 5\n      text:\n        fine: x\n    fast: { skill: SKILL_C, difficulty: 6 }\n", "ENCOUNTER_01: careful.text.fine is not a text this can have; it must be one of attempt, critical, success, partial, failure, disaster.")]
    [InlineData("  approaches:\n    careful: { skill: SKILL_A, difficulty: 5 }\n    fast:\n      skill: SKILL_C\n      difficulty: 6\n      text:\n        failure: \"\"\n", "ENCOUNTER_01: fast.text.failure must not be empty.")]
    [InlineData("  approaches:\n    careful: { skill: SKILL_A, difficulty: 5 }\n    fast: { skill: SKILL_C, difficulty: 6 }\n  decision:\n    id: DECISION_PROMPT_1\n    stakes: 1\n    bold: []\n    safe: []\n    text:\n      bold: \"An open # mark.\"\n", "ENCOUNTER_01: decision.text.bold cannot be read: A # at character 9 is never closed.")]
    public void Own_text_that_can_never_work_is_refused_where_it_sits(string body, string message)
    {
        ContentLoadResult result = LoadEncounter1(body);

        Assert.Contains(result.Errors, e => e.File == "encounters/base.yaml" && e.Message.StartsWith(message, StringComparison.Ordinal));
    }

    [Fact]
    public void Symbols_load_and_a_line_may_use_them()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["symbols/base.yaml"] = "- id: hesitates\n  options: [WORD_A, \"#actor.they# pause\"]\n";
        files["lines/test.yaml"] = OneLine("{ beat: check }", "\"#actor# #hesitates#.\"");

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Empty(result.Errors);
        Assert.Equal(["WORD_A", "#actor.they# pause"], result.Pack!.Symbols.Single().Options);
    }

    [Theory]
    [InlineData("- id: loop\n  options: [\"#loop#\"]\n", "uses itself")]
    [InlineData("- id: a\n  options: [\"#b#\"]\n- id: b\n  options: [\"#a#\"]\n", "uses itself")]
    [InlineData("- id: actor\n  options: [WORD_A]\n", "cannot be called 'actor'")]
    [InlineData("- id: empty\n  options: []\n", "at least one option")]
    [InlineData("- id: Upper\n  options: [WORD_A]\n", "must start with a lower case letter")]
    [InlineData("- id: broken\n  options: [\"open # mark\"]\n", "never closed")]
    public void A_symbol_that_cannot_expand_is_refused(string text, string message)
    {
        ContentLoadResult result = LoadWith("symbols/base.yaml", text);

        Assert.Contains(result.Errors, e => e.File == "symbols/base.yaml" && e.Message.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void A_symbol_used_by_a_line_is_checked_against_that_lines_beat()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["symbols/base.yaml"] = "- id: aside\n  options: [\"#helper# watches\"]\n";
        files["lines/test.yaml"] = OneLine("{ beat: stage }", "\"#aside#.\"");

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Contains(result.Errors, e => e.Message.Contains("symbol 'aside' #helper# names 'helper', which a stage beat does not have", StringComparison.Ordinal));
    }

    [Fact]
    public void Crew_pronouns_must_name_a_set_and_they_always_exists()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files.Remove("pronouns/base.yaml");
        files["crew/starting.yaml"] = files["crew/starting.yaml"].Replace("- id: CREW_01\n", "- id: CREW_01\n  pronouns: they\n", StringComparison.Ordinal).Replace("- id: CREW_02\n", "- id: CREW_02\n  pronouns: xe\n", StringComparison.Ordinal);

        ContentLoadResult result = ContentLoader.Load(files);

        Assert.Equal(["crew/starting.yaml: CREW_02: unknown pronoun set 'xe'."], result.Errors.Select(e => $"{e.File}: {e.Message}"));
    }

    [Fact]
    public void A_crew_member_gets_the_set_they_name()
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        files["crew/starting.yaml"] = files["crew/starting.yaml"].Replace("- id: CREW_02\n", "- id: CREW_02\n  pronouns: she\n", StringComparison.Ordinal);

        ContentPack pack = ContentLoader.Load(files).Pack!;

        Assert.Equal("she", pack.PronounsOf(pack.Crew.Single(c => c.Id == "CREW_02")).Subject);
        Assert.Equal("they", pack.PronounsOf(pack.Crew.Single(c => c.Id == "CREW_01")).Subject);
    }

    [Theory]
    [InlineData("- id: she\n  subject: she\n  object: her\n  possessive: her\n  possessivePronoun: hers\n", "reflexive must be one word")]
    [InlineData("- id: she\n  subject: \" she\"\n  object: her\n  possessive: her\n  possessivePronoun: hers\n  reflexive: herself\n", "subject must be one word")]
    [InlineData("- id: She\n  subject: she\n  object: her\n  possessive: her\n  possessivePronoun: hers\n  reflexive: herself\n", "must start with a lower case letter")]
    public void A_pronoun_set_missing_a_form_is_refused(string text, string message)
    {
        ContentLoadResult result = LoadWith("pronouns/base.yaml", text);

        Assert.Contains(result.Errors, e => e.File == "pronouns/base.yaml" && e.Message.Contains(message, StringComparison.Ordinal));
    }

    // Every situation holds only what the catalogue lists for its kind, and across many missions every listed fact turns up.
    [Fact]
    public void Situations_hold_exactly_the_catalogues_facts_people_and_things()
    {
        var seen = new Dictionary<string, HashSet<string>>();
        foreach (CallAnswer answer in Enum.GetValues<CallAnswer>())
        {
            for (ulong seed = 1; seed <= 150; seed++)
            {
                Team team = seed % 3 == 0 ? TestContent.Team(7, 7, 8) : TestContent.Team(1, 1, 3, 5);
                var parameters = new Parameters(DevelopJuniors: seed % 2 == 0, Autonomy: (int)(seed % 3) - 1);
                MissionState start = MissionResolver.Start(TestContent.Objective, TestContent.Pack.Encounters, team, TestContent.Gear, parameters, TestContent.Tuning, seed);
                MissionStep step = MissionResolver.Advance(start);
                var beats = new List<Beat>(step.Beats);
                while (step.Pending is { } call)
                {
                    step = MissionResolver.Advance(step.State, new CallReply(call.CallId, call.Options.Contains(answer) ? answer : call.LeadPrefers));
                    beats.AddRange(step.Beats);
                }

                var frame = new BeatFrame(start, team.LeadId, TestContent.Objective.Tags, id => new Person(id, $"N_{id}", PronounSet.They), (kind, id) => $"{kind}:{id}");

                IReadOnlyList<Situation> situations = BeatSituations.Of(beats, frame);

                Assert.Equal(beats.Count, situations.Count);
                foreach (Situation s in situations)
                {
                    BeatSpec spec = NarrativeFacts.Beats[s.Beat];
                    // "best SKILL" is a speaker a script can name, not a role of the beat.
                    Assert.All(s.People.Keys.Where(r => !r.StartsWith(Scripts.BestPrefix, StringComparison.Ordinal)), role => Assert.Contains(role, spec.People));
                    Assert.All(s.Words.Keys, thing => Assert.Contains(thing, spec.Things));
                    foreach ((string fact, FactValue value) in s.Facts)
                    {
                        FactSpec? factSpec = NarrativeFacts.Fact(s.Beat, fact);
                        Assert.True(factSpec is not null, $"{s.Beat} has '{fact}', which the catalogue does not list.");
                        Assert.Equal(factSpec!.Type, value.Type);
                        if (factSpec.Domain == FactDomain.Words)
                        {
                            Assert.Contains(value.Text!, factSpec.Words!);
                        }
                    }

                    HashSet<string> kind = seen.TryGetValue(s.Beat, out HashSet<string>? found) ? found : seen[s.Beat] = [];
                    kind.UnionWith(s.Facts.Keys.Where(k => !k.Contains('.', StringComparison.Ordinal)));
                    kind.UnionWith(s.People.Keys.Where(r => !r.StartsWith(Scripts.BestPrefix, StringComparison.Ordinal)).Select(p => "person:" + p));
                }
            }
        }

        foreach ((string beat, HashSet<string> facts) in seen)
        {
            BeatSpec spec = NarrativeFacts.Beats[beat];
            // The fixture's decisions never roll injuries, so an effects beat's injuryRoll is the one fact these missions cannot show.
            IEnumerable<string> expected = spec.Facts.Select(f => f.Name).Concat(spec.People.Select(p => "person:" + p))
                .Where(f => !(beat == "effects" && f == "injuryRoll"));
            Assert.All(expected, f => Assert.True(facts.Contains(f), $"No {beat} beat ever had '{f}'."));
        }

        Assert.Contains("check", seen.Keys);
        Assert.Contains("answer", seen.Keys);
    }

    [Fact]
    public void Every_enum_value_has_its_catalogue_word()
    {
        Assert.Equal(NarrativeFacts.Tiers, Enum.GetValues<Tier>().Select(BeatSituations.Word));
        Assert.Equal(NarrativeFacts.Approaches, Enum.GetValues<Approach>().Select(BeatSituations.Word));
        Assert.Equal(NarrativeFacts.Severities, Enum.GetValues<InjurySeverity>().Select(BeatSituations.Word));
        Assert.Equal(NarrativeFacts.InjuryOutcomes, Enum.GetValues<InjuryOutcome>().Select(BeatSituations.Word));
        Assert.Equal(NarrativeFacts.Answers, Enum.GetValues<CallAnswer>().Select(BeatSituations.Word));
        Assert.Equal(NarrativeFacts.Standings, Enum.GetValues<CallStanding>().Select(BeatSituations.Word));
        Assert.Equal(NarrativeFacts.Outcomes, Enum.GetValues<MissionOutcome>().Select(BeatSituations.Word));
    }
}

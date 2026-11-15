using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Missions;

namespace Sim.Tests;

// A line's text as a script: speech, notes, narration, and the report's sentence; who may speak; and what the loader refuses.
public sealed class ScriptTests
{
    [Fact]
    public void One_sentence_is_a_script_of_one_narration_entry_and_is_its_own_report_sentence()
    {
        Script script = Scripts.Parse("#lead# reached #encounter#.");

        Assert.Equal([new ScriptEntry(EntryKind.Narration, null, "#lead# reached #encounter#.")], script.Entries);
        Assert.Equal("#lead# reached #encounter#.", script.ReportText);
    }

    [Fact]
    public void Each_line_of_a_script_is_speech_a_note_narration_or_the_reports_sentence()
    {
        Script script = Scripts.Parse("lead: Captain, we are here.\nbest SKILL_A:  Nothing on the scanner.\n\n> Signal falling.\nNote: this is narration.\nThe wind rose.\n= #lead# called in.");

        Assert.Equal(
            [
                new ScriptEntry(EntryKind.Speech, "lead", "Captain, we are here."),
                new ScriptEntry(EntryKind.Speech, "best SKILL_A", "Nothing on the scanner."),
                new ScriptEntry(EntryKind.Note, null, "Signal falling."),
                new ScriptEntry(EntryKind.Narration, null, "Note: this is narration."),
                new ScriptEntry(EntryKind.Narration, null, "The wind rose."),
            ],
            script.Entries);
        Assert.Equal("#lead# called in.", script.Summary);
        Assert.Equal("SKILL_A", Scripts.BestSkill("best SKILL_A"));
        Assert.Null(Scripts.BestSkill("lead"));
    }

    [Fact]
    public void A_script_of_several_entries_has_no_report_sentence_unless_it_gives_one_and_a_dash_says_nothing()
    {
        Assert.Null(Scripts.Parse("lead: One.\nlead: Two.").ReportText);
        Assert.Null(Scripts.Parse("> One note.").ReportText);
        Assert.Empty(Scripts.Parse(" - ").Entries);
    }

    [Theory]
    [InlineData("= Only a summary.", "A script needs at least one line besides the report's sentence.")]
    [InlineData("= First.\nlead: After.", "The report's sentence (the line starting with =) must be the last line.")]
    [InlineData("lead: Hello.\n>", "The note after > is empty.")]
    [InlineData("\n \n", "The text is empty.")]
    public void A_script_that_cannot_be_read_says_why(string text, string message)
    {
        NarrativeFormatException e = Assert.Throws<NarrativeFormatException>(() => Scripts.Parse(text));

        Assert.StartsWith(message, e.Message, StringComparison.Ordinal);
    }

    private static ContentLoadResult LoadEncounter1(string body)
    {
        Dictionary<string, string> files = TestContent.RepoFiles();
        int start = files["encounters/base.yaml"].IndexOf("- id: ENCOUNTER_01\n", StringComparison.Ordinal);
        int end = files["encounters/base.yaml"].IndexOf("- id: ENCOUNTER_02\n", StringComparison.Ordinal);
        files["encounters/base.yaml"] = files["encounters/base.yaml"][..start] + "- id: ENCOUNTER_01\n  tags: [field, hazard]\n  chain: ENCOUNTER_CHAIN\n" + body + files["encounters/base.yaml"][end..];
        return ContentLoader.Load(files);
    }

    private const string Approaches = "  approaches:\n    careful: { skill: SKILL_A, difficulty: 5 }\n    fast: { skill: SKILL_C, difficulty: 6 }\n";

    private const string Decision = "  decision:\n    id: DECISION_PROMPT_1\n    stakes: 1\n    bold: [{ momentum: 1 }]\n    safe: []\n";

    [Fact]
    public void An_encounter_holds_scripts_an_attempt_and_the_labels_of_its_call()
    {
        ContentLoadResult result = LoadEncounter1(
            "  text:\n    arrive: |-\n      lead: Captain, we are at #encounter#.\n      best SKILL_A: Nothing on the scanner.\n      captain: Go on.\n      > Signal falling.\n      = #lead# reached #encounter#.\n"
            + "  approaches:\n    careful:\n      skill: SKILL_A\n      difficulty: 5\n      text:\n        attempt: \"actor: I will take it slowly.\"\n        disaster: \"-\"\n    fast: { skill: SKILL_C, difficulty: 6 }\n"
            + Decision + "    labels: { bold: Send them in, withdraw: Pull them out }\n");

        Assert.Empty(result.Errors);
        Dictionary<string, LineDef> owned = result.Pack!.Lines.Where(l => l.Owner == "ENCOUNTER_01").ToDictionary(l => l.Id);
        Assert.Equal("lead: Captain, we are at #encounter#.\nbest SKILL_A: Nothing on the scanner.\ncaptain: Go on.\n> Signal falling.\n= #lead# reached #encounter#.", owned["ENCOUNTER_01.arrive"].Text);
        LineDef attempt = owned["ENCOUNTER_01.careful.attempt"];
        Assert.Equal("attempt", attempt.Beat);
        Assert.Equal(["encounter=ENCOUNTER_01", "approach=careful"], attempt.When.Select(c => $"{c.Fact}={c.Values.Single()}"));
        Assert.Equal("check", owned["ENCOUNTER_01.careful.disaster"].Beat);
        Assert.Equal(new Dictionary<string, string> { ["bold"] = "Send them in", ["withdraw"] = "Pull them out" }, result.Pack.ChoiceLabels["DECISION_PROMPT_1"]);
    }

    [Theory]
    [InlineData("  text:\n    arrive: \"actor: I am not here yet.\"\n" + Approaches, "ENCOUNTER_01: text.arrive cannot be used: speaker 'actor' is not in a stage beat, which has lead.")]
    [InlineData("  text:\n    arrive: \"best SKILL_NONE: Hello.\"\n" + Approaches, "ENCOUNTER_01: text.arrive cannot be used: speaker 'best SKILL_NONE' names 'SKILL_NONE', a skill the pack does not have.")]
    [InlineData("  text:\n    arrive: \"lead: Hello.\\n= #actor# arrived.\"\n" + Approaches, "ENCOUNTER_01: text.arrive cannot be used: #actor# names 'actor', which a stage beat does not have.")]
    [InlineData("  text:\n    arrive: \"= Only this.\"\n" + Approaches, "ENCOUNTER_01: text.arrive cannot be read: A script needs at least one line besides the report's sentence.")]
    [InlineData(Approaches + Decision + "    labels: { maybe: Perhaps }\n", "ENCOUNTER_01: decision.labels.maybe is not an answer; it must be one of bold, safe, withdraw.")]
    [InlineData(Approaches + Decision + "    labels: { bold: \"This label runs on for far longer than a button can hold\" }\n", "ENCOUNTER_01: decision.labels.bold must be 1 to 40 characters on one line, with no spaces at either end.")]
    public void A_script_or_a_label_that_can_never_work_is_refused_where_it_sits(string body, string message)
    {
        ContentLoadResult result = LoadEncounter1(body);

        Assert.Contains(result.Errors, e => e.File == "encounters/base.yaml" && e.Message.StartsWith(message, StringComparison.Ordinal));
    }

    // Standing counts who can still act, and "best SKILL" names someone still standing who is not the lead.
    [Fact]
    public void A_situation_counts_who_is_standing_and_names_the_best_at_each_skill()
    {
        int checkedBeats = 0;
        for (ulong seed = 1; seed <= 300 && checkedBeats == 0; seed++)
        {
            Team team = TestContent.Team(1, 1, 3, 5);
            MissionState start = MissionResolver.Start(TestContent.Objective, TestContent.Pack.Encounters, team, TestContent.Gear, new Parameters(Risk: 1), TestContent.Tuning, seed);
            MissionStep step = MissionResolver.Advance(start);
            var beats = new List<Beat>(step.Beats);
            while (step.Pending is { } call)
            {
                step = MissionResolver.Advance(step.State, new CallReply(call.CallId, call.Options[0]));
                beats.AddRange(step.Beats);
            }

            if (!beats.Any(b => b is InjuryBeat { Outcome: InjuryOutcome.Dead or InjuryOutcome.Incapacitated }))
            {
                continue;
            }

            var frame = new BeatFrame(start, team.LeadId, TestContent.Objective.Tags, id => new Person(id, $"N_{id}", PronounSet.They), (kind, id) => $"{kind}:{id}");
            IReadOnlyList<Situation> situations = BeatSituations.Of(beats, frame);
            var down = new HashSet<string>();
            Assert.Equal(team.Members.Count, situations[0].Facts["teamStanding"].Number);
            for (int i = 0; i < beats.Count; i++)
            {
                if (beats[i] is InjuryBeat { Outcome: InjuryOutcome.Dead or InjuryOutcome.Incapacitated } injury)
                {
                    down.Add(injury.CrewId);
                }

                Assert.Equal(team.Members.Count - down.Count, situations[i].Facts["teamStanding"].Number);
                foreach ((string role, Person person) in situations[i].People.Where(p => p.Key.StartsWith(Scripts.BestPrefix, StringComparison.Ordinal)))
                {
                    Assert.DoesNotContain(person.Id, down);
                    Assert.NotEqual(situations[i].People["lead"].Id, person.Id);
                    Assert.True(team.Members.Single(m => m.Id == person.Id).Skills[role[Scripts.BestPrefix.Length..]] > 0);
                }

                checkedBeats++;
            }
        }

        Assert.True(checkedBeats > 0, "No seed took a crew member out.");
    }

    [Fact]
    public void An_attempt_is_the_check_without_its_result()
    {
        var check = new Situation(
            "check",
            new Dictionary<string, FactValue> { ["approach"] = FactValue.Of("careful"), ["tier"] = FactValue.Of("failure"), ["margin"] = FactValue.Of(-3), ["actor.id"] = FactValue.Of("CREW_01") },
            new Dictionary<string, Person> { ["actor"] = new("CREW_01", "N", PronounSet.They) },
            new Dictionary<string, string> { ["skill"] = "S" });

        Situation attempt = BeatSituations.Attempt(check);

        Assert.Equal("attempt", attempt.Beat);
        Assert.Equal(["actor.id", "approach"], attempt.Facts.Keys.Order());
        Assert.Same(check.People, attempt.People);
        Assert.All(attempt.Facts.Keys, f => Assert.NotNull(NarrativeFacts.Fact("attempt", f)));
    }
}

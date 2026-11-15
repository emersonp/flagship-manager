using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;
using Web.Components;

namespace Web.Tests;

// The mission as prose (slice 1d): authored lines with tags in the log, the structural line as detail, and the aftermath report.
public sealed class ProseTests : IDisposable
{
    private static readonly ContentPack Pack = BridgePack.Wide(BridgePack.Load());

    private readonly TestApp app = new();

    public void Dispose()
    {
        app.Dispose();
    }

    private static Played Ended(ContentPack pack, Func<Played, bool>? also = null)
    {
        for (ulong seed = 1; seed <= 80; seed++)
        {
            var played = new Played(pack, seed);
            if (played.StepUntil(() => played.State.Phase == CyclePhase.Aftermath && played.Mission?.State.Result is not null && (also?.Invoke(played) ?? true)))
            {
                return played;
            }
        }

        throw new InvalidOperationException("No seed reached the wanted state.");
    }

    private static BridgeView Read(Played played) => BridgeReader.Read(played.Record(), played.Pack, played.CycleEvents());

    private static List<MissionBeat> Beats(Played played) => played.CycleEvents().OfType<MissionBeat>().Where(b => b.MissionId == played.Mission!.Id).OrderBy(b => b.Index).ToList();

    private static string Join(IReadOnlyList<LinePart> parts) => string.Concat(parts.Select(p => p.Text));

    [Fact]
    public void Every_log_line_has_authored_prose_and_keeps_its_structural_text()
    {
        Played played = Ended(Pack);
        List<MissionBeat> beats = Beats(played);

        IReadOnlyList<LogLine> log = Read(played).Mission!.Log;

        Assert.Equal(beats.Count, log.Count);
        for (int i = 0; i < log.Count; i++)
        {
            Assert.True(log[i].Authored, $"Beat {i} ({log[i].Kind}) fell back to the systems line.");
            Assert.Equal(BeatLines.Line(beats[i].Index, beats[i].Beat, log.Take(i + 1).Count(l => l.Kind == "stage"), new Names(Pack)).Text, log[i].Text);
            Assert.NotEqual(log[i].Text, Join(log[i].Prose!));
        }
    }

    [Fact]
    public void A_check_line_names_its_actor_and_tags_its_tier_margin_and_changes()
    {
        Played played = Ended(Pack);
        var names = new Names(Pack);
        List<MissionBeat> beats = Beats(played);
        IReadOnlyList<LogLine> log = Read(played).Mission!.Log;

        for (int i = 0; i < log.Count; i++)
        {
            if (beats[i].Beat is not CheckBeat check)
            {
                continue;
            }

            Assert.Contains(log[i].Prose!, p => p.IsName && p.Text == names.Crew(check.ActorId));
            Assert.Equal($"{BeatLines.Words(check.Tier)} {check.Margin.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture).Replace('-', '−')}", log[i].Tags![0]);
            Assert.Equal(check.MomentumAfter != check.Momentum, log[i].Tags!.Any(t => t.StartsWith("momentum ", StringComparison.Ordinal)));
            Assert.Equal(check.StrainAfter != check.Strain, log[i].Tags!.Any(t => t.StartsWith("strain ", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void With_no_lines_every_line_is_the_systems_line_and_the_report_is_empty()
    {
        Played played = Ended(Pack with { Lines = [] });

        BridgeView view = Read(played);

        Assert.All(view.Mission!.Log, l =>
        {
            Assert.False(l.Authored);
            Assert.Equal(l.Text, Join(l.Prose!));
        });
        Assert.Empty(view.Aftermath!.Report!.Sentences);
    }

    [Fact]
    public void The_same_view_reads_the_same_every_time()
    {
        Played played = Ended(Pack);

        Assert.Equal(Read(played).Mission!.Log.Select(l => Join(l.Prose!)), Read(played).Mission!.Log.Select(l => Join(l.Prose!)));
    }

    [Fact]
    public void A_line_is_chosen_by_salience_so_a_more_specific_one_wins_where_it_holds()
    {
        LineDef specific = new("LINE_CHECK_PLACE", "check", [new CriterionDef("tier", ["critical", "success", "partial", "failure", "disaster"]), new CriterionDef("helped", ["true", "false"])], "SPECIFIC #actor#.");
        Played played = Ended(Pack with { Lines = [.. Pack.Lines, specific] });

        IReadOnlyList<LogLine> log = Read(played).Mission!.Log;

        Assert.All(log.Where(l => l.Kind == "check"), l => Assert.StartsWith("SPECIFIC ", Join(l.Prose!), StringComparison.Ordinal));
        Assert.Contains(log, l => l.Kind == "check");
    }

    [Fact]
    public void A_line_whose_text_cannot_expand_falls_back_to_the_systems_line()
    {
        // The loader would refuse this; a pack built in code shows the fallback instead of failing the page.
        LineDef broken = new("LINE_CHECK_BROKEN", "check", [new CriterionDef("helped", ["true", "false"]), new CriterionDef("supervised", ["true", "false"])], "#nobody# acts.");
        Played played = Ended(Pack with { Lines = [.. Pack.Lines, broken] });

        Assert.All(Read(played).Mission!.Log.Where(l => l.Kind == "check"), l => Assert.False(l.Authored));
    }

    [Fact]
    public void The_report_keeps_the_beats_order_ends_with_the_result_and_stays_within_bounds()
    {
        Played played = Ended(Pack);

        ReportView report = Read(played).Aftermath!.Report!;

        Assert.InRange(report.Sentences.Count, Report.Fewest, Report.Most);
        Assert.Equal("The mission ended.", Join(report.Sentences[^1]));
    }

    [Fact]
    public void Injuries_and_calls_always_make_the_report_and_calls_are_listed_with_their_answers()
    {
        Played played = Ended(Pack, p => p.CycleEvents().OfType<MissionBeat>().Any(b => b.Beat is CallBeat) && p.CycleEvents().OfType<MissionBeat>().Any(b => b.Beat is InjuryBeat { Outcome: not InjuryOutcome.Unharmed }));
        List<MissionBeat> beats = Beats(played);

        ReportView report = Read(played).Aftermath!.Report!;

        List<string> sentences = report.Sentences.Select(Join).ToList();
        Assert.Contains(sentences, s => s.Contains(" called in: ", StringComparison.Ordinal));
        Assert.Contains(sentences, s => s.Contains(" injured at ", StringComparison.Ordinal) || s.Contains(" was incapacitated at ", StringComparison.Ordinal) || s.Contains(" was killed at ", StringComparison.Ordinal));
        Assert.Equal(beats.Count(b => b.Beat is CallBeat) + beats.Count(b => b.Beat is NoticeBeat), report.Decisions.Count);
        Assert.Contains(report.Decisions.Select(Join), d => d.Contains("; the captain answered ", StringComparison.Ordinal));
    }

    // The report is a part of the log: every sentence in it is the sentence the log showed for that beat.
    [Fact]
    public void The_report_says_what_the_log_said()
    {
        Played played = Ended(Pack);

        BridgeView view = Read(played);

        List<string> log = view.Mission!.Log.Select(l => Join(l.Prose!)).ToList();
        Assert.All(view.Aftermath!.Report!.Sentences.Select(Join), s => Assert.Contains(s, log));
    }

    // An encounter's own words win over the shared lines for its beats, and other encounters keep the shared ones.
    [Fact]
    public void An_encounters_own_text_wins_at_that_encounter_only()
    {
        Played plain = Ended(Pack);
        string encounter = plain.CycleEvents().OfType<MissionBeat>().Select(b => b.Beat).OfType<StageBeat>().First().EncounterId;
        LineDef own = new($"{encounter}.arrive", "stage", [new CriterionDef("encounter", [encounter])], "OWN #encounter#.", null, encounter);
        Played played = Ended(Pack with { Lines = [.. Pack.Lines, own] }, p => p.CycleEvents().OfType<MissionBeat>().Any(b => b.Beat is StageBeat s && s.EncounterId == encounter));
        List<MissionBeat> beats = Beats(played);

        IReadOnlyList<LogLine> log = Read(played).Mission!.Log;

        for (int i = 0; i < log.Count; i++)
        {
            if (beats[i].Beat is StageBeat stage)
            {
                Assert.Equal(stage.EncounterId == encounter, Join(log[i].Prose!).StartsWith("OWN ", StringComparison.Ordinal));
            }
        }

        Assert.DoesNotContain(own.Id, Web.Forms.LineLint.Of(Pack with { Lines = [own] }).NeverChosen);
    }

    // A script is shown entry by entry with its speakers; the report says its sentence after "=", never the dialogue.
    [Fact]
    public void A_script_plays_in_the_log_by_speaker_and_the_report_says_its_own_sentence()
    {
        Played plain = Ended(Pack);
        string encounter = plain.CycleEvents().OfType<MissionBeat>().Select(b => b.Beat).OfType<StageBeat>().First().EncounterId;
        LineDef own = new($"{encounter}.arrive", "stage", [new CriterionDef("encounter", [encounter])], "lead: We are at #encounter#.\ncaptain: Go on.\nactor: Nobody is acting yet.\n> Signal falling.\n= SUMMARY #lead#.", null, encounter);
        LineDef result = new("LINE_RESULT_SCRIPT", "result", [new CriterionDef("outcome", Sim.Core.Narrative.NarrativeFacts.Outcomes)], "lead: It is over.\ncaptain: Go on home.\n= SUMMARY #lead#.");
        Played played = Ended(Pack with { Lines = [.. Pack.Lines, own, result] }, p => p.CycleEvents().OfType<MissionBeat>().Any(b => b.Beat is StageBeat s && s.EncounterId == encounter));
        List<MissionBeat> beats = Beats(played);

        BridgeView view = Read(played);

        LogLine line = view.Mission!.Log[beats.FindIndex(b => b.Beat is StageBeat s && s.EncounterId == encounter)];
        Assert.True(line.IsScript);
        // The actor has no part in a stage beat, so that entry is left out and the rest plays.
        Assert.Equal([Sim.Core.Narrative.EntryKind.Speech, Sim.Core.Narrative.EntryKind.Speech, Sim.Core.Narrative.EntryKind.Note], line.Script!.Select(e => e.Kind));
        Assert.Equal(Prose.CaptainName, line.Script![1].Speaker);
        Assert.NotNull(line.Script[0].Speaker);
        Assert.Null(line.Script[2].Speaker);
        Assert.Equal("Signal falling.", Join(line.Script[2].Parts));
        List<string> report = view.Aftermath!.Report!.Sentences.Select(Join).ToList();
        Assert.StartsWith("SUMMARY ", report[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(report, s => s.Contains("Go on", StringComparison.Ordinal));
    }

    // A script with no sentence for the report leaves the beat to the shared line, so the report still has it.
    [Fact]
    public void A_script_with_no_report_sentence_leaves_the_report_to_the_shared_line()
    {
        LineDef script = new("LINE_RESULT_SCRIPT", "result", [new CriterionDef("outcome", Sim.Core.Narrative.NarrativeFacts.Outcomes)], "lead: It is over.\ncaptain: Come home.");
        Played played = Ended(Pack with { Lines = [.. Pack.Lines, script] });

        BridgeView view = Read(played);

        Assert.True(view.Mission!.Log[^1].IsScript);
        string shared = Join(Read(Ended(Pack)).Aftermath!.Report!.Sentences[^1]);
        Assert.Equal(shared, Join(view.Aftermath!.Report!.Sentences[^1]));
    }

    // What an approach says before its check is shown first, then what the check's own line says.
    [Fact]
    public void An_attempt_is_said_before_the_checks_result()
    {
        LineDef attempt = new("LINE_ATTEMPT", "attempt", [], "actor: ATTEMPT.");
        Played played = Ended(Pack with { Lines = [.. Pack.Lines, attempt] });
        List<MissionBeat> beats = Beats(played);

        IReadOnlyList<LogLine> log = Read(played).Mission!.Log;

        for (int i = 0; i < log.Count; i++)
        {
            if (beats[i].Beat is CheckBeat)
            {
                Assert.Equal(2, log[i].Script!.Count);
                Assert.Equal("ATTEMPT.", Join(log[i].Script![0].Parts));
                Assert.Equal(Sim.Core.Narrative.EntryKind.Narration, log[i].Script![1].Kind);
            }
            else
            {
                Assert.False(log[i].IsScript);
            }
        }
    }

    [Fact]
    public async Task The_log_and_the_call_show_a_script_and_the_call_its_own_button_labels()
    {
        List<ProseEntry> script = [new(Sim.Core.Narrative.EntryKind.Speech, "NAME_A", [new(false, "We are here.")]), new(Sim.Core.Narrative.EntryKind.Note, null, [new(false, "Signal <falling>.")])];
        var line = new LogLine(2, "call", "NAME_A calls: DECISION_A. Stakes 2.", Prose: [new(false, "x")], Authored: true, Script: script);
        var call = new CallView("CALL_1", "DECISION_A", 2, ["Bold", "Safe", "Withdraw"], "Safe", "CREW_01") { Script = script, Labels = new Dictionary<string, string> { ["bold"] = "Send them in", ["safe"] = "Hold here" } };
        Render render = app.Services.GetRequiredService<Render>();

        string log = await render.Html<MissionLog>(new Dictionary<string, object?> { ["Lines"] = new List<LogLine> { line } });
        string panel = await render.Html<CallPanel>(new Dictionary<string, object?> { ["LegacyId"] = Guid.NewGuid(), ["MissionId"] = "MISSION_1", ["Bar"] = new TopBarView("LEGACY", 1, 1, CyclePhase.Resolution, null), ["Call"] = call });

        foreach (string html in new[] { log, panel })
        {
            Assert.Contains("<span class=\"script__who\">NAME_A</span>", html, StringComparison.Ordinal);
            Assert.Contains("script__line--note", html, StringComparison.Ordinal);
            Assert.Contains("Signal &lt;falling&gt;.", html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("beat__prose", log, StringComparison.Ordinal);
        Assert.Contains(">Send them in</button>", panel, StringComparison.Ordinal);
        Assert.Contains(">Hold here</button>", panel, StringComparison.Ordinal);
        Assert.Contains(">Withdraw</button>", panel, StringComparison.Ordinal);
        Assert.Contains("The lead prefers: Hold here", panel, StringComparison.Ordinal);
    }

    // The lint asks for report lines only where the report would use one, so the two rules must agree.
    [Fact]
    public void The_report_rule_reads_the_same_from_a_beat_and_from_its_situation()
    {
        int missions = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var played = new Played(Pack, seed);
            if (!played.StepUntil(() => played.State.Phase == CyclePhase.Aftermath && played.Mission?.State.Result is not null))
            {
                continue;
            }

            missions++;
            List<MissionBeat> beats = Beats(played);
            var names = new Names(Pack);
            var prose = new Prose(Pack, names, played.State.Seed, played.Mission!, beats, played.Mission!.State.Team[0].Member.Id);

            Assert.Equal(beats.Select(b => Report.Reports(b.Beat)), prose.Situations.Select(Report.Reports));
        }

        Assert.True(missions >= 8);
    }

    [Fact]
    public void The_lint_names_a_line_another_always_beats_and_beats_with_no_line()
    {
        LineDef result = new("LINE_RESULT_ANY", "result", [new CriterionDef("outcome", Sim.Core.Narrative.NarrativeFacts.Outcomes)], "Over.");

        Web.Forms.LineLint shadowed = Web.Forms.LineLint.Of(Pack with { Lines = [.. Pack.Lines, result] });
        Web.Forms.LineLint empty = Web.Forms.LineLint.Of(Pack with { Lines = [] });

        Assert.Contains("LINE_RESULT", shadowed.NeverChosen);
        Assert.DoesNotContain("LINE_RESULT_ANY", shadowed.NeverChosen);
        Assert.Contains("check", empty.Without);
        Assert.Contains("effects", empty.Without);
        Assert.Contains("result", empty.ReportWithout);
        Assert.DoesNotContain("effects", empty.ReportWithout);
        Assert.False(empty.Clean);
    }

    [Fact]
    public async Task The_log_shows_prose_then_tags_and_the_roll_on_request()
    {
        var check = new LogLine(3, "check", "NAME_A checks SKILL_A at PLACE_A: 6 + 3 against 15, failure (−6).", Parts: [new(true, "NAME_A"), new(false, " checks SKILL_A at PLACE_A: 6 + 3 against 15, failure (−6).")], Prose: [new(true, "NAME_A <b>"), new(false, " slips.")], Tags: ["failure −6", "strain +2"], Authored: true);
        var plain = new LogLine(4, "result", "Result: loss.", Prose: [new(false, "Result: loss.")], Tags: ["loss"]);

        string html = await app.Services.GetRequiredService<Render>().Html<MissionLog>(new Dictionary<string, object?> { ["Lines"] = new List<LogLine> { check, plain } });

        Assert.Contains("<span class=\"beat__prose\">NAME_A &lt;b&gt; slips.</span>", html, StringComparison.Ordinal);
        html = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("<span class=\"fig-tag\">failure −6</span>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"fig-tag\">strain +2</span>", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"beat__detail\"><summary>Roll</summary>", html, StringComparison.Ordinal);
        Assert.Contains("6 + 3 against 15", System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""), StringComparison.Ordinal);
        // The systems line is shown as it is, with no disclosure repeating it.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<details"));
        Assert.Contains("Result: loss.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_aftermath_shows_the_report_and_its_decisions()
    {
        var report = new ReportView([[new(false, "SENTENCE_A.")], [new(true, "NAME_A"), new(false, " SENTENCE_B.")]], [[new(false, "DECISION_A: answered.")]]);
        var aftermath = new AftermathView("Loss", [], [], report);

        string html = await app.Services.GetRequiredService<Render>().Html<AftermathScreen>(new Dictionary<string, object?> { ["LegacyId"] = Guid.NewGuid(), ["Bar"] = new TopBarView("LEGACY", 1, 1, CyclePhase.Aftermath, null), ["Aftermath"] = aftermath });

        Assert.Contains("<p class=\"report__text\">SENTENCE_A. NAME_A SENTENCE_B.</p>", html, StringComparison.Ordinal);
        Assert.Contains("DECISION_A: answered.", html, StringComparison.Ordinal);
    }
}

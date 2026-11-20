using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Sim.Core;
using Web.Bridge;
using Web.Components;

namespace Web.Tests;

// Components drawn from hand-made views: each one reads the view's data, never its display text.
public sealed class ComponentTests : IDisposable
{
    private static readonly TopBarView Bar = new("LEGACY", 1, 1, CyclePhase.Aftermath, null);

    private readonly TestApp app = new();

    public void Dispose()
    {
        app.Dispose();
    }

    private Task<string> Html<T>(Dictionary<string, object?> parameters)
        where T : IComponent
    {
        return app.Services.GetRequiredService<Render>().Html<T>(parameters);
    }

    private static CrewRow Crew(string id, string status, int level, int injuries)
    {
        return new CrewRow(id, 1, status, status == "Active", [new SkillCell("SKILL_A", level, Math.Max(0, level - injuries))], Enumerable.Repeat(9, injuries).ToList(), null, 50, 1, HealsIn: injuries > 0 ? ["1 week"] : []);
    }

    // A template the editor deleted while the site offers it: the row says so and offers no radio to launch with.
    [Fact]
    public async Task A_briefing_objective_whose_template_is_gone_says_so_and_is_not_offered()
    {
        var kept = new ObjectiveView("o_1_1", "OBJECTIVE_TEMPLATE_1", ["SKILL_A"], 2, 4, 3, false);
        var gone = new ObjectiveView("o_1_2", "OBJECTIVE_LOST", [], 0, 0, null, false, Missing: true);
        var briefing = new BriefingView([kept, gone], [Crew("CREW_01", "Active", 3, 0)], [], 2);

        string html = await Html<BriefingScreen>(new() { ["LegacyId"] = Guid.NewGuid(), ["Bar"] = Bar with { Phase = CyclePhase.Briefing }, ["Briefing"] = briefing });

        Assert.Contains("id=\"objective-o_1_1\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"objective-o_1_2\"", html, StringComparison.Ordinal);
        Assert.Contains("No longer in the content", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-skills=\"\"", html, StringComparison.Ordinal);
    }

    // With one objective left to launch, it is chosen at the start, even beside a gone one.
    [Fact]
    public async Task The_only_objective_still_in_the_content_is_chosen_at_the_start()
    {
        var kept = new ObjectiveView("o_1_1", "OBJECTIVE_TEMPLATE_1", ["SKILL_A"], 2, 4, 3, false);
        var gone = new ObjectiveView("o_1_2", "OBJECTIVE_LOST", [], 0, 0, null, false, Missing: true);
        var briefing = new BriefingView([kept, gone], [Crew("CREW_01", "Active", 3, 0)], [], 2);

        string html = await Html<BriefingScreen>(new() { ["LegacyId"] = Guid.NewGuid(), ["Bar"] = Bar with { Phase = CyclePhase.Briefing }, ["Briefing"] = briefing });

        Assert.Matches("<input[^>]*id=\"objective-o_1_1\"[^>]*checked", html);
        Assert.Contains("data-signals:_min=\"2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_route_reads_each_stop_from_the_stage_lines_data_not_its_text()
    {
        LogLine[] lines = [new(0, "stage", "TEXT_1", "ENCOUNTER_01", true, 1, 2), new(1, "check", "TEXT_2"), new(2, "stage", "TEXT_3", "ENCOUNTER_02", false, 2, 2)];

        string html = await Html<StageRoute>(new() { ["Lines"] = lines });

        Assert.Equal(2, Regex.Matches(html, "class=\"stop\"").Count);
        Assert.Contains("<span class=\"stop__id\">ENCOUNTER_01</span><span class=\"stop__note\">Comms blocked</span>", html);
        Assert.Contains("<span class=\"stop__id\">ENCOUNTER_02</span></li>", html);
        Assert.DoesNotContain("TEXT_", html);
    }

    // Once the mission has ended, no stop is the current one.
    [Fact]
    public async Task An_ended_route_has_no_current_stop()
    {
        LogLine[] lines = [new(0, "stage", "TEXT_1", "ENCOUNTER_01", false, 1, 2), new(1, "result", "TEXT_2")];

        string running = await Html<StageRoute>(new() { ["Lines"] = lines });
        string ended = await Html<StageRoute>(new() { ["Lines"] = lines, ["Ended"] = true });

        Assert.Contains("aria-current=\"step\"", running);
        Assert.Contains(", current", running);
        Assert.DoesNotContain("aria-current", ended);
        Assert.DoesNotContain("current", ended);
    }

    [Fact]
    public async Task The_aftermath_labels_each_change_by_its_kind_and_marks_injury_and_death_as_harm()
    {
        var aftermath = new AftermathView("PartialSuccess", [new("CREW_01", "skill", "TEXT_1"), new("CREW_02", "injury", "TEXT_2"), new("CREW_03", "death", "TEXT_3"), new("CREW_04", "recovery", "TEXT_4"), new("CREW_05", "loyalty", "TEXT_5")], []);

        string html = await Html<AftermathScreen>(new() { ["LegacyId"] = Guid.NewGuid(), ["Bar"] = Bar, ["Aftermath"] = aftermath });

        List<string> items = Regex.Matches(html, "<li[^>]*>.*?</li>").Select(m => m.Value).ToList();
        Assert.Equal(["skill", "injury", "death", "recovery", "loyalty"], items.Select(i => Regex.Match(i, "changes__kind\">([^<]+)<").Groups[1].Value));
        Assert.Equal([false, true, true, false, false], items.Select(i => i.StartsWith("<li class=\"is-harm\"", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_aftermath_head_names_the_team_and_an_unattended_line_drops_the_objectives_own_id()
    {
        var mission = new MissionView("m_1_1", "o_1_1", ["CREW_01", "CREW_02"], "CREW_02", 1, 1, [], null, "PartialSuccess", [], 5, "CREW_01");
        var aftermath = new AftermathView("PartialSuccess", [], [new("o_1_2", "OBJECTIVE_TEMPLATE_2", 6, 15, false)]);

        string html = await Html<AftermathScreen>(new() { ["LegacyId"] = Guid.NewGuid(), ["Bar"] = Bar, ["Aftermath"] = aftermath, ["Mission"] = mission, ["ObjectiveName"] = "OBJECTIVE_TEMPLATE_1" });

        Assert.Contains("<p>Outcome of OBJECTIVE_TEMPLATE_1, team CREW_01 and CREW_02 (lead)</p>", html);
        Assert.Contains("OBJECTIVE_TEMPLATE_2, rolled", html);
        Assert.DoesNotContain("o_1_2", html);
    }

    [Fact]
    public async Task A_dead_members_levels_are_plain_and_an_injured_level_reads_with_a_space()
    {
        CrewRow[] rows = [Crew("CREW_01", "Active", 5, 1), Crew("CREW_02", "Dead", 6, 2)];

        string html = System.Net.WebUtility.HtmlDecode(await Html<Roster>(new() { ["Rows"] = rows }));

        string injured = Regex.Match(html, "<tr data-crew=\"CREW_01\".*?</tr>", RegexOptions.Singleline).Value;
        string dead = Regex.Match(html, "<tr data-crew=\"CREW_02\".*?</tr>", RegexOptions.Singleline).Value;
        Assert.Contains("<td class=\"n\">5 <span class=\"eff\">(4)</span></td>", injured);
        Assert.Contains("<td class=\"n\">6</td>", dead);
        Assert.DoesNotContain("eff", dead);
    }

    [Theory]
    [InlineData(0, "0 entries, newest first")]
    [InlineData(1, "1 entry, newest first")]
    [InlineData(3, "3 entries, newest first")]
    public async Task The_log_counts_its_entries_inside_the_element_the_streams_patch(int count, string words)
    {
        List<LogLine> lines = Enumerable.Range(0, count).Select(i => new LogLine(i, "check", $"TEXT_{i}")).ToList();

        string html = await Html<MissionLog>(new() { ["Lines"] = lines });

        Assert.StartsWith("<section id=\"mission-log\"", html);
        Assert.EndsWith("</section>", html.TrimEnd());
        Assert.Contains(Regex.Replace(words, "^(\\d+)", "<span class=\"m\">$1</span>"), html);
        Assert.Equal(count, Regex.Matches(html, "<li ").Count);
    }

    private const string Odd = "NAME 7 <b> & -3";

    private static string Encoded => "NAME 7 &lt;b&gt; &amp; -3";

    [Fact]
    public async Task The_roster_shows_names_and_skill_names_encoded_while_the_row_key_stays_the_id()
    {
        CrewRow row = Crew("CREW_01", "Active", 5, 0) with { Name = Odd, Skills = [new SkillCell("SKILL_A", 5, 5, "SKILL " + Odd)] };

        string html = await Html<Roster>(new() { ["Rows"] = new[] { row } });

        Assert.Contains("<tr data-crew=\"CREW_01\"", html);
        Assert.Contains($"<td class=\"who\">{Encoded}</td>", html);
        Assert.Contains($"SKILL {Encoded}</th>", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public async Task The_route_shows_the_encounters_name_when_the_line_has_one()
    {
        LogLine[] lines = [new(0, "stage", "TEXT_1", "ENCOUNTER_01", false, 1, 2, null, Odd)];

        string html = await Html<StageRoute>(new() { ["Lines"] = lines });

        Assert.Contains($"<span class=\"stop__id\">{Encoded}</span>", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public async Task The_log_sets_a_names_digits_as_plain_text_and_the_lines_own_figures_as_figures()
    {
        Sim.Core.Content.ContentPack pack = BridgePack.Load();
        var names = new Names(pack with { Crew = [new Sim.Core.Content.CrewDef("CREW_02", 1, new Dictionary<string, int>(), 0, 0, Odd)] });
        LogLine line = BeatLines.Line(0, new Sim.Missions.LeadChangeBeat("CREW_02"), 0, names);
        LogLine call = BeatLines.Line(1, new Sim.Missions.CallBeat(new Sim.Missions.PendingCall("DECISION_1", 3, 2.5, "CREW_02", Sim.Missions.CallAnswer.Safe, [Sim.Missions.CallAnswer.Safe], "c1")), 0, names);

        string html = await Html<MissionLog>(new() { ["Lines"] = new[] { line, call } });

        Assert.Contains($"{Encoded} becomes lead.", html);
        Assert.Contains($"{Encoded} calls: DECISION_1. Stakes <span class=\"m\">3</span>.", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<span class=\"m\">7", html);
    }

    [Fact]
    public async Task The_call_panel_shows_the_lead_and_decision_names_encoded()
    {
        var call = new CallView("c1", "DECISION_1", 2, ["Bold", "Safe"], "Safe", "CREW_01", Odd, Odd + " lead");

        string html = await Html<CallPanel>(new() { ["LegacyId"] = Guid.NewGuid(), ["MissionId"] = "m_1_1", ["Bar"] = Bar, ["Call"] = call });

        Assert.Contains($"{Encoded} lead is calling", html);
        Assert.Contains($"<dd>{Encoded}</dd>", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public async Task The_team_line_and_gear_show_names_and_mark_the_lead_by_id()
    {
        var mission = new MissionView("m_1_1", "o_1_1", ["CREW_01", "CREW_02"], "CREW_02", 1, 1, [], null, null, ["GEAR_01"], 5, "CREW_01", ["NAME_A", Odd], ["NAME_G"]);

        string html = await Html<ResolutionScreen>(new() { ["LegacyId"] = Guid.NewGuid(), ["Bar"] = Bar, ["Mission"] = mission });

        Assert.Contains($"<dd id=\"team\">NAME_A and {Encoded} (lead)</dd>", html);
        Assert.Contains("<dd>NAME_G</dd>", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public async Task The_aftermath_head_and_changes_show_names_and_an_unattended_line_shows_the_template_name()
    {
        var mission = new MissionView("m_1_1", "o_1_1", ["CREW_01", "CREW_02"], "CREW_02", 1, 1, [], null, "PartialSuccess", [], 5, "CREW_01", ["NAME_A", Odd]);
        var change = new CrewChange("CREW_02", "death", $"{Odd} died.", [new LinePart(true, Odd), new LinePart(false, " died.")]);
        var aftermath = new AftermathView("PartialSuccess", [change], [new("o_1_2", "OBJECTIVE_TEMPLATE_2", 6, 15, false, Odd)]);

        string html = await Html<AftermathScreen>(new() { ["LegacyId"] = Guid.NewGuid(), ["Bar"] = Bar, ["Aftermath"] = aftermath, ["Mission"] = mission, ["ObjectiveName"] = Odd });

        Assert.Contains($"Outcome of {Encoded}, team NAME_A and {Encoded} (lead)", html);
        Assert.Contains($"<span>{Encoded} died.</span>", html);
        Assert.Contains($"<p>{Encoded}, rolled", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public async Task Rows_marked_to_disambiguate_show_their_id_in_the_roster_and_the_briefing_and_in_the_labels()
    {
        CrewRow[] crew = [Crew("CREW_01", "Active", 5, 0) with { Name = "SAME", Disambiguate = true }, Crew("CREW_02", "Active", 5, 0) with { Name = "SAME", Disambiguate = true }, Crew("CREW_03", "Active", 5, 0) with { Name = "UNIQUE" }];
        GearChoice[] gear = [new("GEAR_01", "scanner", "SAME", true), new("GEAR_02", "tools", "SAME", true), new("GEAR_03", "comms", "UNIQUE")];
        var briefing = new BriefingView([], crew, gear, 2);

        string roster = await Html<Roster>(new() { ["Rows"] = crew });
        string page = await Html<BriefingScreen>(new() { ["LegacyId"] = Guid.NewGuid(), ["Bar"] = Bar, ["Briefing"] = briefing });

        Assert.Contains("<td class=\"who\">SAME <span class=\"quiet\">CREW_02</span></td>", roster);
        Assert.Contains("<td class=\"who\">UNIQUE</td>", roster);
        Assert.Contains("<td class=\"who\">SAME <span class=\"quiet\">CREW_01</span></td>", page);
        Assert.Contains("aria-label=\"Team SAME (CREW_02)\"", page);
        Assert.Contains("aria-label=\"Lead SAME (CREW_01)\"", page);
        Assert.Contains("aria-label=\"Team UNIQUE\"", page);
        Assert.Contains("<span class=\"gear__id\">SAME <span class=\"quiet\">GEAR_02</span></span>", page);
        Assert.Contains("<span class=\"gear__id\">UNIQUE</span>", page);
    }
}

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
        return new CrewRow(id, 1, status, status == "Active", [new SkillCell("SKILL_A", level, Math.Max(0, level - injuries))], Enumerable.Repeat(9, injuries).ToList(), null, 50, 1);
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
    [InlineData(0, "0 entries, newest last")]
    [InlineData(1, "1 entry, newest last")]
    [InlineData(3, "3 entries, newest last")]
    public async Task The_log_counts_its_entries_inside_the_element_the_streams_patch(int count, string words)
    {
        List<LogLine> lines = Enumerable.Range(0, count).Select(i => new LogLine(i, "check", $"TEXT_{i}")).ToList();

        string html = await Html<MissionLog>(new() { ["Lines"] = lines });

        Assert.StartsWith("<section id=\"mission-log\"", html);
        Assert.EndsWith("</section>", html.TrimEnd());
        Assert.Contains(Regex.Replace(words, "^(\\d+)", "<span class=\"m\">$1</span>"), html);
        Assert.Equal(count, Regex.Matches(html, "<li ").Count);
    }
}

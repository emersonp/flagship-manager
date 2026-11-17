using System.Net;
using System.Text.RegularExpressions;
using Persistence;
using Sim.Core;
using Sim.Crew;
using Sim.Missions;

namespace Web.Tests;

public sealed class BriefingTests : IDisposable
{
    private readonly TestApp app = new();
    private readonly HttpClient client;

    public BriefingTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private async Task<Guid> NewLegacy()
    {
        HttpResponseMessage created = await client.PostAsync("/legacies", new FormUrlEncodedContent([new KeyValuePair<string, string>("name", "LEGACY_ONE")]));
        Guid id = Guid.Parse(created.Headers.Location!.ToString().Split('/')[^1]);
        // A cycle opens on the captain's itinerary (1e part two); these tests start at its orders.
        await Post(id, "advance");
        return id;
    }

    private async Task<string> Post(Guid id, string action, IEnumerable<KeyValuePair<string, string>>? form = null)
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{id}/{action}", new FormUrlEncodedContent(app.Stamped(id, form ?? [])));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<Guid> NewBriefing()
    {
        Guid id = await NewLegacy();
        await Post(id, "advance");
        return id;
    }

    private async Task<string> Page(Guid id)
    {
        return await client.GetStringAsync($"/legacy/{id}");
    }

    // The section a heading id labels, up to its closing tag.
    private static string Section(string html, string headingId)
    {
        int heading = html.IndexOf($"id=\"{headingId}\"", StringComparison.Ordinal);
        Assert.True(heading >= 0, $"no heading {headingId}");
        int start = html.LastIndexOf("<section", heading, StringComparison.Ordinal);
        int end = html.IndexOf("</section>", heading, StringComparison.Ordinal);
        return html[start..end];
    }

    [Fact]
    public async Task The_away_team_table_has_no_scrolling_wrapper()
    {
        string card = Section(await Page(await NewBriefing()), "h-crew");
        Assert.DoesNotContain("class=\"tw\"", card);
        Assert.Contains("class=\"pick pick--crew\"", card);
    }

    private static IReadOnlyList<string> Values(string html, string field)
    {
        return Regex.Matches(html, $"name=\"{field}\" value=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
    }

    private static List<KeyValuePair<string, string>> Valid(string objective, IEnumerable<string> crew, string lead, IEnumerable<string>? gear = null, string priority = "0")
    {
        var form = new List<KeyValuePair<string, string>> { new("objective", objective), new("lead", lead), new("priority", priority), new("autonomy", "0") };
        form.AddRange(crew.Select(c => new KeyValuePair<string, string>("crew", c)));
        form.AddRange((gear ?? []).Select(g => new KeyValuePair<string, string>("gear", g)));
        return form;
    }

    private async Task<(Guid Id, List<KeyValuePair<string, string>> Form, IReadOnlyList<string> Crew, IReadOnlyList<string> Gear)> Ready()
    {
        Guid id = await NewBriefing();
        string page = await Page(id);
        IReadOnlyList<string> crew = Values(page, "crew");
        IReadOnlyList<string> gear = Values(page, "gear");
        return (id, Valid(Values(page, "objective")[0], crew.Take(2), crew[0]), crew, gear);
    }

    private (int Events, CyclePhase Phase, int Cycle, int Week, long Next) Snapshot(Guid id)
    {
        LegacyRecord record = app.Game.Store.Load(id, app.Live.Game.ModuleTypes)!;
        return (app.Game.Store.ReadEvents(id).Count, record.State.Phase, record.State.Cycle, record.State.Week, record.State.NextSequence);
    }

    private static void AssertRefusal(string stream, string reason)
    {
        Assert.Single(Regex.Matches(stream, "event: datastar-patch-elements"));
        Assert.Contains("id=\"refusal\"", stream);
        Assert.Contains("role=\"alert\"", stream);
        Assert.Contains(reason, stream);
    }

    [Fact]
    public async Task Orders_show_the_sites_objectives_and_the_briefing_button()
    {
        Guid id = await NewLegacy();

        string html = await Page(id);

        Assert.Contains("<table class=\"site\">", html);
        Assert.Contains("OBJECTIVE_TEMPLATE_", html);
        Assert.Contains("<p id=\"refusal\" role=\"alert\"></p>", html);
        Assert.Contains("To briefing", html);
        Assert.Contains($"@post(&#x27;/legacy/{id}/advance&#x27;, {{contentType: &#x27;form&#x27;}})", html);
    }

    [Fact]
    public async Task Orders_and_briefing_give_the_objectives_own_id_its_own_column()
    {
        Guid id = await NewLegacy();
        string orders = await Page(id);
        await Post(id, "advance");
        string briefing = await Page(id);

        foreach (string html in new[] { orders, briefing })
        {
            Assert.Contains("<th scope=\"col\" class=\"oid\">Id</th>", html);
            Assert.Matches("<td class=\"oid\">o_1_1</td>", html);
            Assert.DoesNotMatch("OBJECTIVE_TEMPLATE_\\d+\\s*<span class=\"note\">o_", html);
            Assert.Contains("<th scope=\"col\" class=\"n wide\">Team</th>", html);
            Assert.Contains("<th scope=\"col\" class=\"n wide\">Unattended difficulty</th>", html);
            Assert.Matches("<td class=\"skills\"><span class=\"nw\">SKILL_", html);
        }
    }

    [Fact]
    public async Task To_briefing_moves_the_phase_and_streams_the_top_bar_then_main_then_roster()
    {
        Guid id = await NewLegacy();

        string stream = await Post(id, "advance");

        Assert.Equal(3, Regex.Matches(stream, "event: datastar-patch-elements").Count);
        int top = stream.IndexOf("id=\"topbar\"", StringComparison.Ordinal);
        int main = stream.IndexOf("id=\"main\"", StringComparison.Ordinal);
        int roster = stream.IndexOf("id=\"roster\"", StringComparison.Ordinal);
        Assert.True(top >= 0 && top < main && main < roster);
        Assert.Contains("Briefing", stream);
        Assert.Equal(CyclePhase.Briefing, Snapshot(id).Phase);
    }

    [Fact]
    public async Task The_briefing_lists_eight_crew_and_disables_an_unfit_one()
    {
        Guid id = await NewBriefing();
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            legacy.State.Module<CrewState>(CrewModule.Name).Members[0].Status = CrewStatus.Recovering;
            return legacy;
        });

        string html = await Page(id);

        Assert.Equal(8, Values(html, "crew").Count);
        Assert.Matches("<input type=\"checkbox\" id=\"crew-[^\"]+\" name=\"crew\" value=\"[^\"]+\" aria-label=\"[^\"]+\" disabled", html);
        Assert.Single(Regex.Matches(html, "name=\"crew\"[^>]* disabled"));
        Assert.Contains("<form id=\"briefing\"", html);
        Assert.Contains("<p id=\"refusal\" role=\"alert\"></p>", html);
    }

    [Fact]
    public async Task A_valid_launch_reaches_resolution_and_stores_the_team_assigned()
    {
        (Guid id, var form, _, _) = await Ready();

        string stream = await Post(id, "launch", form);

        // The launch now streams its beats after the three opening patches.
        Assert.True(Regex.Matches(stream, "event: datastar-patch-elements").Count > 3);
        Assert.StartsWith("event: datastar-patch-elements\ndata: elements <header id=\"topbar\"", stream);
        Assert.Equal(CyclePhase.Resolution, Snapshot(id).Phase);
        Assert.Contains(app.Game.Store.ReadEvents(id), e => e.Type == "TeamAssigned");
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "no crew", "A team of 0 is outside" },
        { "too many crew", "A team of 5 is outside" },
        { "lead off the team", "is not on the team." },
        { "unfit crew", "is not fit." },
        { "too much gear", "The loadout holds at most" },
        { "bad parameter", "Priority 7 is outside -1 to 1." },
        { "second launch", "A team can only be assigned in the briefing." },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_bad_launch_is_refused_with_the_buss_reason_and_stores_nothing(string kind, string reason)
    {
        (Guid id, var form, IReadOnlyList<string> crew, IReadOnlyList<string> gear) = await Ready();
        string objective = form[0].Value;
        List<KeyValuePair<string, string>> bad = kind switch
        {
            "no crew" => Valid(objective, [], crew[0]),
            "too many crew" => Valid(objective, crew.Take(5), crew[0]),
            "lead off the team" => Valid(objective, crew.Take(2), crew[5]),
            "unfit crew" => Valid(objective, crew.Take(2), crew[0]),
            "too much gear" => Valid(objective, crew.Take(2), crew[0], gear.Concat(gear).Distinct().Take(gear.Count)),
            "bad parameter" => Valid(objective, crew.Take(2), crew[0], priority: "7"),
            _ => form,
        };
        if (kind == "unfit crew")
        {
            await app.Sessions.Run(app.Live, id, legacy =>
            {
                legacy.State.Module<CrewState>(CrewModule.Name).Members.Single(m => m.Id == crew[1]).Status = CrewStatus.Recovering;
                return legacy;
            });
        }

        if (kind == "second launch")
        {
            await Post(id, "launch", form);
        }

        var before = Snapshot(id);
        string stream = await Post(id, "launch", bad);

        AssertRefusal(stream, reason);
        Assert.Equal(before, Snapshot(id));
    }

    // Risk tolerance is gone (approaches plan, Task 4): the briefing does not ask for it.
    [Fact]
    public async Task The_briefing_has_no_risk_tolerance()
    {
        string html = await Page(await NewBriefing());

        Assert.DoesNotContain("name=\"risk\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Risk tolerance", html, StringComparison.Ordinal);
    }

    // A form from an older page may still post risk; it is ignored, not refused.
    [Fact]
    public async Task A_posted_risk_field_is_ignored()
    {
        (Guid id, var form, _, _) = await Ready();
        form.Add(new("risk", "7"));

        await Post(id, "launch", form);

        Assert.Contains(app.Game.Store.ReadEvents(id), e => e.Type == "TeamAssigned");
    }

    [Fact]
    public async Task Sending_nobody_reaches_resolution_with_no_mission()
    {
        Guid id = await NewBriefing();

        string stream = await Post(id, "advance");

        Assert.Equal(3, Regex.Matches(stream, "event: datastar-patch-elements").Count);
        Assert.Equal(CyclePhase.Resolution, Snapshot(id).Phase);
        Assert.DoesNotContain(app.Game.Store.ReadEvents(id), e => e.Type == "TeamAssigned");
    }

    [Theory]
    [InlineData("objective")]
    [InlineData("lead")]
    [InlineData("autonomy")]
    public async Task A_form_missing_a_field_is_refused_as_incomplete_and_stores_nothing(string missing)
    {
        (Guid id, var form, _, _) = await Ready();
        form.RemoveAll(f => f.Key == missing);
        var before = Snapshot(id);

        string stream = await Post(id, "launch", form);

        AssertRefusal(stream, "The form was not complete.");
        Assert.Equal(before, Snapshot(id));
    }

    [Fact]
    public async Task A_parameter_that_is_not_a_number_is_refused_as_incomplete()
    {
        (Guid id, var form, _, _) = await Ready();
        form.RemoveAll(f => f.Key == "autonomy");
        form.Add(new("autonomy", "high"));

        AssertRefusal(await Post(id, "launch", form), "The form was not complete.");
    }

    [Fact]
    public async Task Advancing_from_aftermath_lands_on_the_next_cycles_itinerary_at_week_2()
    {
        Guid id = await NewBriefing();
        await Post(id, "advance");
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != CyclePhase.Aftermath)
            {
                legacy.Send(new AdvancePhase());
            }

            return legacy;
        });

        string stream = await Post(id, "advance");

        var after = Snapshot(id);
        Assert.Equal((CyclePhase.Itinerary, 2, 2), (after.Phase, after.Cycle, after.Week));
        Assert.Contains("<dt>Week</dt><dd>2</dd>", stream);
        Assert.Contains("<dt>Cycle</dt><dd>2</dd>", stream);
        Assert.Matches("aria-current=\"step\"[^>]*><span class=\"phase__no\">1</span><span class=\"phase__name\">Itinerary</span>", stream);
        Assert.Contains("To orders", stream);
    }

    [Fact]
    public async Task Advancing_in_the_wrong_phase_is_a_refusal_from_the_bus()
    {
        Guid id = await NewBriefing();
        await Post(id, "advance");
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != CyclePhase.Advance)
            {
                legacy.Send(new AdvancePhase());
            }

            return legacy;
        });
        var before = Snapshot(id);

        // From the advance phase one advance is valid; a launch there is not.
        string stream = await Post(id, "launch", Valid("X", ["CREW_01", "CREW_02"], "CREW_01"));

        AssertRefusal(stream, "A team can only be assigned in the briefing.");
        Assert.Equal(before, Snapshot(id));
    }

    [Theory]
    [InlineData("advance")]
    [InlineData("launch")]
    public async Task An_unknown_legacy_is_a_404(string action)
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{Guid.NewGuid()}/{action}", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> BriefingWithObjectives(int count)
    {
        for (int i = 0; i < 60; i++)
        {
            Guid id = await NewBriefing();
            string html = await Page(id);
            if (Values(html, "objective").Count == count)
            {
                return html;
            }
        }

        throw new InvalidOperationException("No legacy offered that many objectives.");
    }

    [Fact]
    public async Task Each_objective_radio_carries_its_own_team_range_and_a_single_one_seeds_the_signals()
    {
        string single = await BriefingWithObjectives(1);
        Match one = Regex.Match(single, "name=\"objective\"[^>]* data-min=\"(\\d+)\" data-max=\"(\\d+)\"");
        Assert.True(one.Success);
        Assert.Contains($"data-signals:_min=\"{one.Groups[1].Value}\" data-signals:_max=\"{one.Groups[2].Value}\"", single);

        string many = await BriefingWithObjectives(2);
        MatchCollection ranges = Regex.Matches(many, "name=\"objective\"[^>]* data-min=\"(\\d+)\" data-max=\"(\\d+)\"");
        Assert.Equal(2, ranges.Count);
        Assert.All(ranges, r => Assert.Contains($"team {r.Groups[1].Value} to {r.Groups[2].Value}</span></label>", many));
        Assert.Contains("data-signals:_min=\"0\" data-signals:_max=\"0\"", many);
        Assert.DoesNotContain(" checked", Regex.Match(many, "<fieldset>.*?</fieldset>", RegexOptions.Singleline).Value);
        Assert.Equal(2, Regex.Matches(many, "name=\"objective\"[^>]* required").Count);
    }

    [Fact]
    public async Task Crew_controls_are_named_with_the_crew_id()
    {
        string html = await Page(await NewBriefing());

        Assert.Contains("name=\"crew\" value=\"CREW_01\" aria-label=\"Team CREW_01\"", html);
        Assert.Contains("name=\"lead\" value=\"CREW_01\" aria-label=\"Lead CREW_01\"", html);
    }

    [Fact]
    public async Task A_repeated_lead_field_is_refused_as_incomplete()
    {
        (Guid id, var form, IReadOnlyList<string> crew, _) = await Ready();
        form.Add(new("lead", crew[1]));
        var before = Snapshot(id);

        AssertRefusal(await Post(id, "launch", form), "The form was not complete.");
        Assert.Equal(before, Snapshot(id));
    }

    [Fact]
    public async Task A_launch_for_a_non_guid_legacy_is_a_404()
    {
        HttpResponseMessage response = await client.PostAsync("/legacy/not-a-guid/launch", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_objective_is_refused_with_the_buss_reason_and_stores_nothing()
    {
        (Guid id, _, IReadOnlyList<string> crew, _) = await Ready();
        var before = Snapshot(id);

        AssertRefusal(await Post(id, "launch", Valid("NO_SUCH", crew.Take(2), crew[0])), "Objective &#x27;NO_SUCH&#x27; is not at this site.");
        Assert.Equal(before, Snapshot(id));
    }
}

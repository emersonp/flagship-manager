using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace Web.Tests;

public sealed class ResolutionTests : IDisposable
{
    private readonly TestApp app = new();
    private readonly HttpClient client;

    public ResolutionTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    // The stream's patch-elements events as their html, in order.
    private static List<string> Patches(string stream)
    {
        return stream.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => string.Join("\n", block.Split('\n').Where(l => l.StartsWith("data: elements ", StringComparison.Ordinal)).Select(l => l["data: elements ".Length..])))
            .ToList();
    }

    private static int Items(string html)
    {
        return Regex.Matches(html, "<li ").Count;
    }

    private static List<string> LogPatches(string stream)
    {
        return Patches(stream).Where(p => p.StartsWith("<section id=\"mission-log\"", StringComparison.Ordinal)).ToList();
    }

    private static string Entity(string html)
    {
        return System.Net.WebUtility.HtmlDecode(html);
    }

    private bool Pending(LoadedLegacy legacy)
    {
        return legacy.State.TryModule(MissionsModule.Name, out MissionsState? m) && m.Site?.Mission?.State.Pending is not null;
    }

    private IReadOnlyList<int> StoredBeatIndexes(Guid id)
    {
        return app.Game.Store.ReadEvents(id).Select(app.Live.Game.Events.FromEnvelope).OfType<MissionBeat>().Select(b => b.Index).ToList();
    }

    // The first seed whose launch leaves a call waiting, stopped just after the launch.
    private async Task<(Guid Id, WebCaptain Captain, string Launch)> LaunchedWithCall()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.Sessions.CreateLegacy(app.Live, $"LEGACY_{seed}", seed);
            var captain = new WebCaptain(app, client, id);
            for (int steps = 0; steps < 50; steps++)
            {
                (string route, string stream) = await captain.Step();
                if (route == "launch")
                {
                    if (Pending(await captain.Read()))
                    {
                        return (id, captain, stream);
                    }

                    break;
                }
            }
        }

        throw new InvalidOperationException("No seed to 200 left a call waiting.");
    }

    private static string RosterOf(string page)
    {
        return Regex.Match(page, "<aside id=\"roster\">.*?</aside>", RegexOptions.Singleline).Value;
    }

    private static string WithoutAwayMarks(string roster)
    {
        return Regex.Replace(Regex.Replace(roster, "<span class=\"away-tag tag\">[^<]*</span>", ""), " class=\"is-away\"", "");
    }

    private async Task<string> Page(Guid id)
    {
        return await client.GetStringAsync($"/legacy/{id}");
    }

    private async Task<(string Mission, string Call, string Option)> CallFields(Guid id)
    {
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        legacy.State.TryModule(MissionsModule.Name, out MissionsState? missions);
        MissionInFlight mission = missions!.Site!.Mission!;
        return (mission.Id, mission.State.Pending!.CallId, mission.State.Pending.Options.First().ToString());
    }

    [Fact]
    public async Task A_launch_stream_shows_the_old_log_first_then_grows_the_log_one_item_at_a_time_and_ends_with_a_call()
    {
        (Guid id, _, string stream) = await LaunchedWithCall();

        List<string> patches = Patches(stream);
        Assert.StartsWith("<header id=\"topbar\"", patches[0]);
        Assert.StartsWith("<main id=\"main\"", patches[1]);
        Assert.StartsWith("<aside id=\"roster\"", patches[2]);
        Assert.Equal(0, Items(patches[1]));
        Assert.Contains("<ol id=\"log\"", patches[1]);
        Assert.DoesNotContain("<form", patches[1]);
        Assert.DoesNotContain("To aftermath", patches[1]);
        List<string> logs = LogPatches(stream);
        int stored = StoredBeatIndexes(id).Count;
        Assert.Equal(stored, logs.Count);
        for (int i = 0; i < logs.Count; i++)
        {
            Assert.Equal(i + 1, Items(logs[i]));
            Assert.Contains($"id=\"beat-{i}\"", logs[i]);
        }

        string last = Patches(stream)[^1];
        Assert.StartsWith("<aside id=\"roster\"", last);
        Assert.Contains(Patches(stream), p => p.StartsWith("<div id=\"call\"", StringComparison.Ordinal) && p.Contains("<form"));
    }

    [Fact]
    public async Task A_launch_stream_holds_back_the_meter_and_the_call_until_the_last_beat()
    {
        (_, _, string stream) = await LaunchedWithCall();

        List<string> patches = Patches(stream);
        Assert.Contains("<div id=\"meter\"></div>", patches[1]);
        int lastLog = patches.FindLastIndex(p => p.StartsWith("<section id=\"mission-log\"", StringComparison.Ordinal));
        int meter = patches.FindIndex(p => p.StartsWith("<div id=\"meter\">", StringComparison.Ordinal));
        int call = patches.FindIndex(p => p.StartsWith("<div id=\"call\"", StringComparison.Ordinal));
        Assert.True(meter > lastLog && call > lastLog, "The meter and call come after the last beat.");
        Assert.Contains("Momentum", patches[meter]);
        Assert.Contains("Strain", patches[meter]);
    }

    private static List<int> StageNumbers(string html)
    {
        return Regex.Matches(html, @"Stage (\d+):").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToList();
    }

    // A route never shows a stage that no log patch (or the opening main) has shown yet in the same stream.
    private static void AssertRoutePacesTheLog(string stream)
    {
        List<string> patches = Patches(stream);
        var seen = new HashSet<int>(StageNumbers(patches[1]));
        int routes = 0;
        foreach (string patch in patches.Skip(3))
        {
            if (patch.StartsWith("<section id=\"mission-log\"", StringComparison.Ordinal))
            {
                seen.UnionWith(StageNumbers(patch));
            }
            else if (patch.StartsWith("<div id=\"route\"", StringComparison.Ordinal))
            {
                routes++;
                int shown = Regex.Matches(patch, "class=\"stop\"").Count;
                Assert.Equal(seen.Count, shown);
                Assert.All(Enumerable.Range(1, shown), n => Assert.Contains(n, seen));
            }
        }

        Assert.True(routes > 0 || seen.Count == 0, "A stream with stage beats patches the route.");
    }

    [Fact]
    public async Task A_launch_patches_the_route_with_each_stage_beat_never_ahead_of_the_log()
    {
        (Guid id, _, string stream) = await LaunchedWithCall();

        AssertRoutePacesTheLog(stream);
        List<string> patches = Patches(stream);
        int stageBeats = patches.Where(p => p.StartsWith("<section id=\"mission-log\"", StringComparison.Ordinal)).Select(StageNumbers).Last().Count;
        Assert.Equal(stageBeats, patches.Count(p => p.StartsWith("<div id=\"route\"", StringComparison.Ordinal)));
        Assert.Contains("<div id=\"route\"></div>", patches[1]);
        string page = await Page(id);
        Assert.Equal(stageBeats, Regex.Matches(Regex.Match(page, "<div id=\"route\".*?</ol>", RegexOptions.Singleline).Value, "class=\"stop\"").Count);
    }

    [Fact]
    public async Task An_answer_opens_with_the_stages_already_reached_and_keeps_the_route_behind_the_log()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        string before = await Page(id);
        int reached = Regex.Matches(Regex.Match(before, "<div id=\"route\".*?</ol>", RegexOptions.Singleline).Value, "class=\"stop\"").Count;

        string stream = (await captain.Step()).Stream;

        List<string> patches = Patches(stream);
        Assert.Equal(reached, Regex.Matches(Regex.Match(patches[1], "<div id=\"route\".*?</ol>", RegexOptions.Singleline).Value, "class=\"stop\"").Count);
        AssertRoutePacesTheLog(stream);
    }

    [Fact]
    public async Task An_answer_opens_with_the_call_panel_empty_and_no_answer_buttons()
    {
        (_, WebCaptain captain, _) = await LaunchedWithCall();

        string stream = (await captain.Step()).Stream;

        string main = Patches(stream)[1];
        Assert.StartsWith("<main id=\"main\"", main);
        Assert.Contains("<div id=\"call\"></div>", main);
        Assert.DoesNotContain("<form", main);
        Assert.DoesNotContain("name=\"answer\"", main);
    }

    private static (int Momentum, int Strain) MeterValues(string meter)
    {
        int Value(string id) => int.Parse(Regex.Match(meter, $"aria-labelledby=\"{id}\"[^>]*aria-valuenow=\"(\\d+)\"").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (Value("g-mom"), Value("g-str"));
    }

    // The figures of the newest stage line in a log patch.
    private static (int Momentum, int Strain)? LastStageFigures(string log)
    {
        Match last = Regex.Matches(log, @"Momentum <span class=""m"">(\d+)</span>, strain <span class=""m"">(\d+)</span>|Momentum (\d+), strain (\d+)").LastOrDefault() ?? Match.Empty;
        if (!last.Success)
        {
            return null;
        }

        string momentum = last.Groups[1].Success ? last.Groups[1].Value : last.Groups[3].Value;
        string strain = last.Groups[2].Success ? last.Groups[2].Value : last.Groups[4].Value;
        return (int.Parse(momentum, System.Globalization.CultureInfo.InvariantCulture), int.Parse(strain, System.Globalization.CultureInfo.InvariantCulture));
    }

    // During an answer's beats the meter follows the stage lines the log has shown; after the last beat it is the stored state.
    [Fact]
    public async Task An_answers_meter_shows_only_figures_a_shown_stage_line_stated_and_ends_on_the_stored_state()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.Sessions.CreateLegacy(app.Live, $"LEGACY_{seed}", seed);
            var captain = new WebCaptain(app, client, id);
            for (int steps = 0; steps < 300 && (await captain.Read()).State.Cycle == 1; steps++)
            {
                bool answering = await captain.Peek() is AnswerCall;
                string stream = (await captain.Step()).Stream;
                List<string> patches = Patches(stream);
                int stageBeats = patches.Count(p => p.StartsWith("<div id=\"route\"", StringComparison.Ordinal));
                if (!answering || stageBeats == 0)
                {
                    continue;
                }

                List<string> meters = patches.Skip(3).Where(p => p.StartsWith("<div id=\"meter\"", StringComparison.Ordinal)).ToList();
                Assert.Equal(stageBeats + 1, meters.Count);
                (int, int)? stated = null;
                var during = new List<(int, int)>();
                foreach (string patch in patches.Skip(3))
                {
                    if (patch.StartsWith("<section id=\"mission-log\"", StringComparison.Ordinal))
                    {
                        stated = LastStageFigures(patch) ?? stated;
                    }
                    else if (patch.StartsWith("<div id=\"meter\"", StringComparison.Ordinal) && during.Count < stageBeats)
                    {
                        Assert.NotNull(stated);
                        Assert.Equal(stated.Value, MeterValues(patch));
                        during.Add(MeterValues(patch));
                    }
                }

                LoadedLegacy legacy = await captain.Read();
                legacy.State.TryModule(MissionsModule.Name, out MissionsState? missions);
                MissionState state = missions!.Site!.Mission!.State;
                Assert.Equal((state.Momentum, state.Strain), MeterValues(meters[^1]));
                Assert.True(patches.FindLastIndex(p => p.StartsWith("<div id=\"meter\"", StringComparison.Ordinal)) > patches.FindLastIndex(p => p.StartsWith("<section id=\"mission-log\"", StringComparison.Ordinal)));
                return;
            }
        }

        Assert.Fail("No seed to 200 had an answer whose beats reached a stage.");
    }

    [Fact]
    public async Task A_call_names_its_lead_and_the_head_shows_the_gear_line()
    {
        (Guid id, _, _) = await LaunchedWithCall();
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        legacy.State.TryModule(MissionsModule.Name, out MissionsState? missions);
        MissionState state = missions!.Site!.Mission!.State;

        string page = Entity(await Page(id));

        Assert.Contains($"<h2 id=\"call-head\">{state.Pending!.LeadId} is calling</h2>", page);
        Assert.Matches("<dt>Gear</dt><dd>[^<]+</dd>", page);
    }

    private static string Row(string roster, string crew)
    {
        return Regex.Match(roster, $"<tr data-crew=\"{crew}\".*?</tr>", RegexOptions.Singleline).Value;
    }

    [Fact]
    public async Task The_page_marks_the_away_team_in_the_roster_as_real_text_and_sends_no_style_element()
    {
        (Guid id, _, _) = await LaunchedWithCall();
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        legacy.State.TryModule(MissionsModule.Name, out MissionsState? missions);
        MissionState state = missions!.Site!.Mission!.State;

        string page = await Page(id);

        string roster = RosterOf(page);
        Assert.DoesNotContain("<style", page);
        foreach (TeamMember member in state.Team.Select(m => m.Member))
        {
            string row = Row(roster, member.Id);
            Assert.Contains(member.Id == state.LeadId ? ">Away team, lead</span>" : ">Away team</span>", row);
            Assert.Contains("is-away", row);
            Assert.DoesNotContain("aria-hidden", Regex.Match(row, "<span class=\"away-tag.*?</span>", RegexOptions.Singleline).Value);
        }

        Assert.Equal(state.Team.Count, Regex.Matches(roster, "away-tag").Count);
    }

    // Finds a request whose new beats change the lead; the stream before it must not show the new lead early.
    private static async Task<(string Stream, string OldLead, string NewLead, Guid Id)> RequestWithLeadChange(string wanted)
    {
        using var wide = new TestApp(0, wideDice: true, noCalls: wanted == "launch");
        HttpClient wideClient = wide.NewClient();
        for (ulong seed = 1; seed < 400; seed++)
        {
            Guid id = await wide.Sessions.CreateLegacy(wide.Live, $"LEGACY_{seed}", seed);
            var captain = new WebCaptain(wide, wideClient, id);
            for (int steps = 0; steps < 300 && (await captain.Read()).State.Cycle == 1; steps++)
            {
                ICommand command = await captain.Peek();
                if ((command is AssignTeam ? "launch" : command is AnswerCall ? "answer" : "") != wanted)
                {
                    await captain.Step();
                    continue;
                }

                string oldLead = command switch
                {
                    AssignTeam team => team.LeadId,
                    AnswerCall => LeadOf((await captain.Read()).State),
                    _ => "",
                };
                string stream = (await captain.Step()).Stream;
                string newLead = LeadOf((await captain.Read()).State);
                if (oldLead.Length > 0 && newLead != "" && newLead != oldLead)
                {
                    return (stream, oldLead, newLead, id);
                }
            }
        }

        throw new InvalidOperationException($"No seed to 400 changed the lead inside one {wanted} request.");
    }

    private static string LeadOf(LegacyState state)
    {
        return state.TryModule(MissionsModule.Name, out MissionsState? m) && m.Site?.Mission is { } mission ? mission.State.LeadId : "";
    }

    [Theory]
    [InlineData("launch")]
    [InlineData("answer")]
    public async Task A_lead_change_in_the_new_beats_shows_the_earlier_lead_until_the_stream_ends(string route)
    {
        (string stream, string oldLead, string newLead, _) = await RequestWithLeadChange(route);

        List<string> patches = Patches(stream);
        Assert.Contains($"{oldLead} (lead)", patches[1]);
        Assert.DoesNotContain($"{newLead} (lead)", patches[1]);
        Assert.Contains(">Away team, lead</span>", Row(patches[2], oldLead));
        Assert.DoesNotContain("Away team, lead", Row(patches[2], newLead));
        string team = patches.Last(p => p.StartsWith("<dd id=\"team\"", StringComparison.Ordinal));
        Assert.Contains($"{newLead} (lead)", team);
        Assert.DoesNotContain($"{oldLead} (lead)", team);
        string roster = patches[^1];
        Assert.StartsWith("<aside id=\"roster\"", roster);
        Assert.Contains(">Away team, lead</span>", Row(roster, newLead));
        Assert.DoesNotContain("Away team, lead", Row(roster, oldLead));
        Assert.True(patches.FindLastIndex(p => p.StartsWith("<dd id=\"team\"", StringComparison.Ordinal)) > patches.FindLastIndex(p => p.StartsWith("<section id=\"mission-log\"", StringComparison.Ordinal)));
    }

    // The spine runs on into the call well only while a call waits; otherwise it ends at the last beat.
    [Fact]
    public async Task The_log_spine_ends_at_the_last_beat_unless_a_call_waits()
    {
        (Guid id, WebCaptain captain, string stream) = await LaunchedWithCall();
        Assert.All(LogPatches(stream).SkipLast(1), p => Assert.Contains("log--end", Regex.Match(p, "<ol id=\"log\"[^>]*>").Value));
        Assert.DoesNotContain("log--end", Regex.Match(LogPatches(stream)[^1], "<ol id=\"log\"[^>]*>").Value);
        Assert.DoesNotContain("log--end", Regex.Match(await Page(id), "<ol id=\"log\"[^>]*>").Value);

        while (Pending(await captain.Read()))
        {
            await captain.Step();
        }

        Assert.Contains("log--end", Regex.Match(await Page(id), "<ol id=\"log\"[^>]*>").Value);
    }

    [Fact]
    public async Task The_route_stages_sit_in_one_ordered_list_with_no_scroll_wrapper()
    {
        (Guid id, _, _) = await LaunchedWithCall();

        string page = await Page(id);

        Assert.Matches("<div id=\"route\"><p[^>]*>[^<]*</p><ol class=\"route\"", page);
    }

    [Fact]
    public async Task A_reload_while_a_call_waits_shows_the_call_and_the_whole_log()
    {
        (Guid id, _, _) = await LaunchedWithCall();

        string page = await Page(id);

        Assert.Equal(StoredBeatIndexes(id).Count, Items(Regex.Match(page, "<ol id=\"log\".*?</ol>", RegexOptions.Singleline).Value));
        Assert.Contains("<div id=\"call\"><", page);
        Assert.Contains("name=\"answer\"", page);
        Assert.Contains("Stakes", page);
        Assert.Contains("The lead prefers", page);
        Assert.DoesNotContain("To aftermath", page);
    }

    [Fact]
    public async Task The_call_panel_has_one_small_form_per_option_with_its_hidden_fields()
    {
        (Guid id, _, _) = await LaunchedWithCall();
        (string mission, string call, _) = await CallFields(id);

        string page = Entity(await Page(id));

        Assert.True(Regex.Matches(page, "<form data-on:submit=\"@post\\('/legacy/" + id + "/answer', \\{contentType: 'form'\\}\\)\"").Count >= 2);
        Assert.Contains($"name=\"mission\" value=\"{mission}\"", page);
        Assert.Contains($"name=\"call\" value=\"{call}\"", page);
        Assert.Contains("<p id=\"call-refusal\" role=\"alert\"></p>", page);
    }

    [Fact]
    public async Task Answering_continues_the_log_from_the_next_index_with_none_repeated()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        int before = StoredBeatIndexes(id).Count;

        (string route, string stream) = await captain.Step();

        Assert.Equal("answer", route);
        List<string> logs = LogPatches(stream);
        Assert.NotEmpty(logs);
        Assert.Equal(before + 1, Items(logs[0]));
        for (int i = 0; i < logs.Count; i++)
        {
            Assert.Equal(before + i + 1, Items(logs[i]));
        }

        IReadOnlyList<int> indexes = StoredBeatIndexes(id);
        Assert.Equal(Enumerable.Range(0, indexes.Count), indexes);
        Assert.Equal(indexes.Count - before, logs.Count);
        List<string> patches = Patches(stream);
        Assert.StartsWith("<main id=\"main\"", patches[1]);
        Assert.Equal(before, Items(Regex.Match(patches[1], "<ol id=\"log\".*?</ol>", RegexOptions.Singleline).Value));
    }

    [Fact]
    public async Task A_double_answer_is_refused_and_stores_nothing()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        (string mission, string call, string option) = await CallFields(id);
        KeyValuePair<string, string>[] form = [new("mission", mission), new("call", call), new("answer", option)];
        await captain.Post("answer", form);
        int stored = app.Game.Store.ReadEvents(id).Count;

        bool callWaits = Pending(await captain.Read());

        string second = await captain.Post("answer", form);

        // The page is redrawn from the stored state, with the reason in the call panel when a call waits, else in the bar.
        List<string> patches = Patches(second);
        Assert.Equal(3, patches.Count);
        Assert.Matches(callWaits ? "<p id=\"call-refusal\" role=\"alert\">The pending call is" : "<p id=\"refusal\" role=\"alert\">No mission is waiting for an answer", patches[1]);
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);
    }

    [Fact]
    public async Task Answers_that_are_wrong_for_the_waiting_call_are_refused()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        (string mission, string call, string option) = await CallFields(id);
        int stored = app.Game.Store.ReadEvents(id).Count;

        string wrongCall = await captain.Post("answer", [new("mission", mission), new("call", "nope"), new("answer", option)]);
        string wrongMission = await captain.Post("answer", [new("mission", "nope"), new("call", call), new("answer", option)]);
        string notAnOption = await captain.Post("answer", [new("mission", mission), new("call", call), new("answer", "Sideways")]);
        string numeric = await captain.Post("answer", [new("mission", mission), new("call", call), new("answer", "1")]);
        string missing = await captain.Post("answer", [new("mission", mission), new("call", call)]);

        Assert.Contains("The pending call is", wrongCall);
        Assert.Contains("The waiting mission is", wrongMission);
        Assert.Contains("The form was not complete.", notAnOption);
        Assert.Contains("The form was not complete.", numeric);
        Assert.Contains("The form was not complete.", missing);
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);
    }

    [Fact]
    public async Task A_hand_made_advance_while_a_call_waits_is_refused()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        int stored = app.Game.Store.ReadEvents(id).Count;

        string stream = await captain.Post("advance", []);

        List<string> patches = Patches(stream);
        Assert.Single(patches);
        Assert.Contains("A call is waiting for an answer.", patches[0]);
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);
        Assert.DoesNotContain("/advance", Entity(await Page(id)));
    }

    [Fact]
    public async Task The_mission_ends_and_to_aftermath_appears()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        while (Pending(await captain.Read()))
        {
            await captain.Step();
        }

        string page = Entity(await Page(id));

        Assert.Contains("To aftermath", page);
        Assert.Contains("Result:", page);
        Assert.DoesNotContain("name=\"answer\"", page);
        Assert.Contains("<div id=\"call\"></div>", page);
        string route = Regex.Match(page, "<div id=\"route\">.*?</ol>\\s*</div>", RegexOptions.Singleline).Value;
        Assert.Contains("class=\"stop\"", route);
        Assert.DoesNotContain("aria-current", route);
        Assert.DoesNotContain("current", route);
    }

    [Fact]
    public async Task The_final_answer_stream_offers_to_aftermath_and_clears_the_call()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        string stream = "";
        while (Pending(await captain.Read()))
        {
            stream = (await captain.Step()).Stream;
        }

        List<string> patches = Patches(stream);
        Assert.DoesNotContain("To aftermath", patches[1]);
        Assert.Contains(patches, p => p.StartsWith("<div id=\"call\"></div>", StringComparison.Ordinal));
        Assert.Contains(patches, p => p.Contains("To aftermath"));
        // The route stops reading "current" once the result line has been shown, never before.
        int lastRoute = patches.FindLastIndex(p => p.StartsWith("<div id=\"route\"", StringComparison.Ordinal));
        Assert.True(lastRoute > patches.FindLastIndex(p => p.StartsWith("<section id=\"mission-log\"", StringComparison.Ordinal)));
        Assert.DoesNotContain("current", patches[lastRoute]);
        Assert.All(patches.Take(lastRoute).Where(p => p.StartsWith("<div id=\"route\"", StringComparison.Ordinal) && p.Contains("class=\"stop\"")), p => Assert.Contains(", current", p));
    }

    [Fact]
    public async Task Aftermath_lists_the_outcome_and_the_leads_change()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        while ((await captain.Read()).State.Phase != CyclePhase.Aftermath)
        {
            await captain.Step();
        }

        string page = Entity(await Page(id));

        Assert.Contains("<main id=\"main\"", page);
        Assert.Contains("Outcome", page);
        Assert.Matches("<h1>(Full success|Partial success|Withdrawal|Loss|Incomplete)</h1>", page);
        // The first listed change is the loyalty one, as before the re-theme.
        Assert.Matches("(?s)<ol id=\"changes\"[^>]*>\\s*<li[^>]*>(?:(?!</li>).)*loyalty now", page);
        Assert.Contains("Next cycle", page);
        Assert.Contains("@post('/legacy/" + id + "/advance', {contentType: 'form'})", page);
    }

    [Fact]
    public async Task A_whole_cycle_through_the_requests_returns_to_orders_at_week_two()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        while ((await captain.Read()).State.Cycle == 1)
        {
            await captain.Step();
        }

        string page = await Page(id);

        Assert.Contains("<dt>Week</dt><dd>2</dd>", page);
        Assert.Contains("<dt>Cycle</dt><dd>2</dd>", page);
        Assert.Matches("aria-current=\"step\"[^>]*><span class=\"phase__no\">1</span><span class=\"phase__name\">Orders</span>", page);
        Assert.Contains("To briefing", Entity(page));
    }

    [Fact]
    public async Task Resolution_with_no_team_sent_offers_to_aftermath()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        var captain = new WebCaptain(app, client, id);
        await captain.Post("advance", []);
        await captain.Post("advance", []);

        string page = Entity(await Page(id));

        Assert.Contains("To aftermath", page);
        Assert.Contains("<ol id=\"log\"></ol>", page);
    }

    [Fact]
    public async Task Answer_for_an_unknown_legacy_is_not_found()
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{Guid.NewGuid()}/answer", new FormUrlEncodedContent([new KeyValuePair<string, string>("mission", "m")]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // The opening roster must be the one on screen before the request, or the injuries give the ending away.
    [Fact]
    public async Task An_answer_that_ends_the_mission_opens_with_the_old_roster_and_closes_with_the_new_one()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.Sessions.CreateLegacy(app.Live, $"LEGACY_{seed}", seed);
            var captain = new WebCaptain(app, client, id);
            for (int steps = 0; steps < 300 && (await captain.Read()).State.Cycle == 1; steps++)
            {
                bool answering = await captain.Peek() is AnswerCall;
                string before = RosterOf(await Page(id));
                List<string> patches = Patches((await captain.Step()).Stream);
                if (answering && patches[2] != patches[^1])
                {
                    Assert.StartsWith("<aside id=\"roster\"", patches[2]);
                    Assert.Equal(before, patches[2]);
                    Assert.StartsWith("<aside id=\"roster\"", patches[^1]);
                    return;
                }
            }
        }

        Assert.Fail("No seed to 200 had an answer that changed the roster.");
    }

    [Fact]
    public async Task A_launch_opens_with_no_momentum_strain_call_form_or_aftermath_button()
    {
        (_, _, string stream) = await LaunchedWithCall();

        string main = Patches(stream)[1];

        Assert.DoesNotContain("Momentum", main);
        Assert.DoesNotContain("Strain", main);
        Assert.DoesNotContain("<form", main);
        Assert.DoesNotContain("To aftermath", main);
    }

    [Fact]
    public async Task A_launch_that_runs_to_the_end_opens_with_the_old_roster_and_shows_the_result_only_in_the_last_beat()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.Sessions.CreateLegacy(app.Live, $"LEGACY_{seed}", seed);
            var captain = new WebCaptain(app, client, id);
            for (int steps = 0; steps < 300 && (await captain.Read()).State.Cycle == 1; steps++)
            {
                bool launching = await captain.Peek() is AssignTeam;
                string before = RosterOf(await Page(id));
                string stream = (await captain.Step()).Stream;
                List<string> patches = Patches(stream);
                List<string> logs = LogPatches(stream);
                if (!launching || logs.Count == 0 || !logs[^1].Contains("Result:") || patches[2] == patches[^1])
                {
                    continue;
                }

                // Only the away-team marks may differ: a launch opens with the team already marked.
                Assert.Equal(before, WithoutAwayMarks(patches[2]));
                Assert.DoesNotContain("Result:", patches[1]);
                Assert.DoesNotContain("To aftermath", patches[1]);
                Assert.DoesNotContain("Result:", string.Join("\n", logs.Take(logs.Count - 1)));
                Assert.Contains("To aftermath", string.Join("\n", patches.Skip(3 + logs.Count)));
                return;
            }
        }

        Assert.Fail("No seed to 200 had a launch that ended with no call and changed the roster.");
    }

    // An answer from another tab to a call already answered, with a new call waiting: the page moves to the new call,
    // shows why, and the new call can be answered from the patched page without a reload.
    [Fact]
    public async Task A_refused_answer_to_an_old_call_patches_the_page_to_the_new_call_and_the_next_answer_succeeds()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.Sessions.CreateLegacy(app.Live, $"LEGACY_{seed}", seed);
            var captain = new WebCaptain(app, client, id);
            for (int steps = 0; steps < 50 && !Pending(await captain.Read()) && (await captain.Read()).State.Cycle == 1; steps++)
            {
                await captain.Step();
            }

            if (!Pending(await captain.Read()))
            {
                continue;
            }

            (string mission, string oldCall, string option) = await CallFields(id);
            KeyValuePair<string, string>[] old = [new("mission", mission), new("call", oldCall), new("answer", option)];
            await captain.Post("answer", old);
            if (!Pending(await captain.Read()))
            {
                continue;
            }

            (_, string newCall, _) = await CallFields(id);
            int stored = app.Game.Store.ReadEvents(id).Count;

            List<string> patches = Patches(await captain.Post("answer", old));

            Assert.Equal(3, patches.Count);
            Assert.StartsWith("<header id=\"topbar\"", patches[0]);
            Assert.StartsWith("<main id=\"main\"", patches[1]);
            Assert.StartsWith("<aside id=\"roster\"", patches[2]);
            Assert.Contains($"name=\"call\" value=\"{newCall}\"", Entity(patches[1]));
            Assert.Matches("<p id=\"call-refusal\" role=\"alert\">The pending call is [^<]+</p>", Entity(patches[1]));
            Assert.Contains("<p id=\"refusal\" role=\"alert\"></p>", patches[1]);
            Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);

            // The next answer is built from the patched page alone.
            string patched = Entity(patches[1]);
            var form = new List<KeyValuePair<string, string>>
            {
                new("mission", Regex.Match(patched, "name=\"mission\" value=\"([^\"]+)\"").Groups[1].Value),
                new("call", Regex.Match(patched, "name=\"call\" value=\"([^\"]+)\"").Groups[1].Value),
                new("answer", Regex.Match(patched, "name=\"answer\" value=\"([^\"]+)\"").Groups[1].Value),
                new("cycle", Regex.Match(patched, "name=\"cycle\" value=\"([^\"]+)\"").Groups[1].Value),
                new("phase", Regex.Match(patched, "name=\"phase\" value=\"([^\"]+)\"").Groups[1].Value),
            };
            string next = await captain.Post("answer", form);
            Assert.NotEmpty(LogPatches(next));
            Assert.True(app.Game.Store.ReadEvents(id).Count > stored, "The answer to the new call was stored.");
            return;
        }

        Assert.Fail("No seed to 200 had an answer followed by a second call.");
    }

    [Fact]
    public async Task A_refused_answer_with_no_call_waiting_corrects_the_page_with_the_three_ordinary_patches()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        while (Pending(await captain.Read()))
        {
            await captain.Step();
        }

        int stored = app.Game.Store.ReadEvents(id).Count;
        List<string> patches = Patches(await captain.Post("answer", [new("mission", "m"), new("call", "c"), new("answer", "Bold")]));

        Assert.Equal(3, patches.Count);
        Assert.StartsWith("<header id=\"topbar\"", patches[0]);
        Assert.StartsWith("<main id=\"main\"", patches[1]);
        Assert.StartsWith("<aside id=\"roster\"", patches[2]);
        Assert.Contains("To aftermath", patches[1]);
        Assert.Contains("No mission is waiting for an answer.", patches[1]);
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);
    }

    [Fact]
    public async Task A_refused_answer_after_the_phase_has_moved_on_shows_the_aftermath()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        while ((await captain.Read()).State.Phase != CyclePhase.Aftermath)
        {
            await captain.Step();
        }

        List<string> patches = Patches(await captain.Post("answer", [new("mission", "m"), new("call", "c"), new("answer", "Bold")]));

        Assert.Equal(3, patches.Count);
        Assert.Contains("Next cycle", patches[1]);
    }

    // The commit happens before the first byte, so a client that leaves mid-stream loses nothing.
    [Fact]
    public async Task An_aborted_stream_leaves_the_legacy_readable_and_complete()
    {
        // A beat delay far longer than the wait below: the request can only end in time because the client left.
        using var slow = new TestApp(600_000);
        HttpClient slowClient = slow.NewClient();
        Guid id = await slow.Sessions.CreateLegacy(slow.Live, "LEGACY", 1);
        var captain = new WebCaptain(slow, slowClient, id);
        await captain.Post("advance", []);
        LoadedLegacy legacy = await captain.Read();
        var team = (AssignTeam)SimRunner.AutoCaptain.NextCommand(slow.Live.Game, legacy.State, slow.Live.Pack);
        var form = new List<KeyValuePair<string, string>> { new("objective", team.ObjectiveId), new("lead", team.LeadId), new("priority", "0"), new("risk", "0"), new("autonomy", "0") };
        form.AddRange(team.CrewIds.Select(c => new KeyValuePair<string, string>("crew", c)));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/legacy/{id}/launch") { Content = new FormUrlEncodedContent(slow.Stamped(id, form)) };
        using var cts = new CancellationTokenSource();
        var finished = new TaskCompletionSource();
        slow.Logs.OnEntry = e =>
        {
            if (e.Message.Contains("Request finished") && e.Message.Contains("/launch"))
            {
                finished.TrySetResult();
            }
        };

        HttpResponseMessage response = await slowClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using (Stream body = await response.Content.ReadAsStreamAsync(cts.Token))
        {
            var buffer = new byte[256];
            Assert.True(await body.ReadAsync(buffer, cts.Token) > 0);
        }

        response.Dispose();
        cts.Cancel();

        // The endpoint ends once the client has gone, and says nothing about it.
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.DoesNotContain(slow.Logs.Entries, e => e.Level >= LogLevel.Error);
        string page = await slowClient.GetStringAsync($"/legacy/{id}");
        IReadOnlyList<int> indexes = StoredBeatIndexes(slow, id);
        Assert.NotEmpty(indexes);
        Assert.Equal(indexes.Count, Items(Regex.Match(page, "<ol id=\"log\".*?</ol>", RegexOptions.Singleline).Value));
    }

    private static IReadOnlyList<int> StoredBeatIndexes(TestApp target, Guid id)
    {
        return target.Game.Store.ReadEvents(id).Select(target.Live.Game.Events.FromEnvelope).OfType<MissionBeat>().Select(b => b.Index).ToList();
    }
}

using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;
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
        return Patches(stream).Where(p => p.StartsWith("<ol id=\"log\"", StringComparison.Ordinal)).ToList();
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
        return app.Game.Store.ReadEvents(id).Select(app.Game.Game.Events.FromEnvelope).OfType<MissionBeat>().Select(b => b.Index).ToList();
    }

    // The first seed whose launch leaves a call waiting, stopped just after the launch.
    private async Task<(Guid Id, WebCaptain Captain, string Launch)> LaunchedWithCall()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.Sessions.CreateLegacy($"LEGACY_{seed}", seed);
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

    private async Task<string> Page(Guid id)
    {
        return await client.GetStringAsync($"/legacy/{id}");
    }

    private async Task<(string Mission, string Call, string Option)> CallFields(Guid id)
    {
        LoadedLegacy legacy = (await app.Sessions.Read(id))!;
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
        List<string> logs = LogPatches(stream).Skip(0).ToList();
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
        Assert.Contains("<p id=\"meter\"></p>", patches[1]);
        int lastLog = patches.FindLastIndex(p => p.StartsWith("<ol id=\"log\"", StringComparison.Ordinal));
        int meter = patches.FindIndex(p => p.StartsWith("<p id=\"meter\">", StringComparison.Ordinal));
        int call = patches.FindIndex(p => p.StartsWith("<div id=\"call\"", StringComparison.Ordinal));
        Assert.True(meter > lastLog && call > lastLog, "The meter and call come after the last beat.");
        Assert.Contains("Momentum", patches[meter]);
        Assert.Contains("Strain", patches[meter]);
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
        Assert.Equal(before, Items(patches[1]));
    }

    [Fact]
    public async Task A_double_answer_is_refused_and_stores_nothing()
    {
        (Guid id, WebCaptain captain, _) = await LaunchedWithCall();
        (string mission, string call, string option) = await CallFields(id);
        KeyValuePair<string, string>[] form = [new("mission", mission), new("call", call), new("answer", option)];
        await captain.Post("answer", form);
        int stored = app.Game.Store.ReadEvents(id).Count;

        string second = await captain.Post("answer", form);

        List<string> patches = Patches(second);
        Assert.Single(patches);
        Assert.Contains("id=\"call-refusal\"", patches[0]);
        Assert.Contains("role=\"alert\"", patches[0]);
        Assert.Matches("(No mission is waiting for an answer|The pending call is)", patches[0]);
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
        Assert.DoesNotContain("data-on:click=\"@post('/legacy/" + id + "/advance')\"", Entity(await Page(id)));
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
        Assert.Matches("(FullSuccess|PartialSuccess|Withdrawal|Loss|Incomplete)", page);
        Assert.Matches("<ul id=\"changes\">\\s*<li>[^<]*loyalty now", page);
        Assert.Contains("Next cycle", page);
        Assert.Contains("@post('/legacy/" + id + "/advance')", page);
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

        Assert.Contains("Week 2, Cycle 2, Orders", page);
        Assert.Contains("To briefing", Entity(page));
    }

    [Fact]
    public async Task Resolution_with_no_team_sent_offers_to_aftermath()
    {
        Guid id = await app.Sessions.CreateLegacy("LEGACY", 1);
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

    // The commit happens before the first byte, so a client that leaves mid-stream loses nothing.
    [Fact]
    public async Task An_aborted_stream_leaves_the_legacy_readable_and_complete()
    {
        using var slow = new TestApp(5000);
        HttpClient slowClient = slow.NewClient();
        Guid id = await slow.Sessions.CreateLegacy("LEGACY", 1);
        var captain = new WebCaptain(slow, slowClient, id);
        await captain.Post("advance", []);
        LoadedLegacy legacy = await captain.Read();
        var team = (AssignTeam)SimRunner.AutoCaptain.NextCommand(slow.Game.Game, legacy.State, slow.Game.Pack);
        var form = new List<KeyValuePair<string, string>> { new("objective", team.ObjectiveId), new("lead", team.LeadId), new("priority", "0"), new("risk", "0"), new("autonomy", "0") };
        form.AddRange(team.CrewIds.Select(c => new KeyValuePair<string, string>("crew", c)));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/legacy/{id}/launch") { Content = new FormUrlEncodedContent(form) };
        using var cts = new CancellationTokenSource();

        HttpResponseMessage response = await slowClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        using (Stream body = await response.Content.ReadAsStreamAsync(cts.Token))
        {
            var buffer = new byte[256];
            Assert.True(await body.ReadAsync(buffer, cts.Token) > 0);
        }

        response.Dispose();
        cts.Cancel();

        string page = await slowClient.GetStringAsync($"/legacy/{id}");
        IReadOnlyList<int> indexes = StoredBeatIndexes(slow, id);
        Assert.NotEmpty(indexes);
        Assert.Equal(indexes.Count, Items(Regex.Match(page, "<ol id=\"log\".*?</ol>", RegexOptions.Singleline).Value));
    }

    private static IReadOnlyList<int> StoredBeatIndexes(TestApp target, Guid id)
    {
        return target.Game.Store.ReadEvents(id).Select(target.Game.Game.Events.FromEnvelope).OfType<MissionBeat>().Select(b => b.Index).ToList();
    }
}

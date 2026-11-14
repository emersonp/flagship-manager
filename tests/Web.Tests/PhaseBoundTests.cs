using System.Net;
using System.Text.RegularExpressions;
using Persistence;
using Sim.Core;
using Sim.Missions;

namespace Web.Tests;

// Every action carries the cycle and phase its page was drawn at; a page that is out of date stores nothing and is redrawn.
public sealed class PhaseBoundTests : IDisposable
{
    private const string Notice = "This page was out of date. It has been refreshed.";

    private readonly TestApp app = new();
    private readonly HttpClient client;

    public PhaseBoundTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private static List<string> Patches(string stream)
    {
        return stream.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => string.Join("\n", block.Split('\n').Where(l => l.StartsWith("data: elements ", StringComparison.Ordinal)).Select(l => l["data: elements ".Length..])))
            .ToList();
    }

    // Posts exactly the fields given: no stamp is added.
    private async Task<string> Post(Guid id, string route, IEnumerable<KeyValuePair<string, string>> form)
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{id}/{route}", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private (int Events, CyclePhase Phase, int Cycle, long Next) Snapshot(Guid id)
    {
        LegacyRecord record = app.Game.Store.Load(id, app.Live.Game.ModuleTypes)!;
        return (app.Game.Store.ReadEvents(id).Count, record.State.Phase, record.State.Cycle, record.State.NextSequence);
    }

    // The three ordinary patches from the stored state, with the notice in the main screen's refusal.
    private static void AssertRefreshed(string stream, CyclePhase phase)
    {
        List<string> patches = Patches(stream);
        Assert.Equal(3, patches.Count);
        Assert.StartsWith("<header id=\"topbar\"", patches[0]);
        Assert.Matches($"aria-current=\"step\"[^>]*><span class=\"phase__no\">\\d</span><span class=\"phase__name\">{phase}</span>", patches[0]);
        Assert.StartsWith("<main id=\"main\"", patches[1]);
        Assert.Contains($"<p id=\"refusal\" role=\"alert\">{Notice}</p>", patches[1]);
        Assert.StartsWith("<aside id=\"roster\"", patches[2]);
    }

    // Orders, then send nobody: resolution with no mission, offering "To aftermath".
    private async Task<Guid> ResolutionWithNobodySent()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        await Post(id, "advance", app.Stamp(id));
        await Post(id, "advance", app.Stamp(id));
        Assert.Equal(CyclePhase.Resolution, Snapshot(id).Phase);
        return id;
    }

    [Fact]
    public async Task A_double_click_on_to_briefing_moves_one_phase_and_the_second_click_refreshes_the_page()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        List<KeyValuePair<string, string>> stamp = app.Stamp(id);
        await Post(id, "advance", stamp);
        var before = Snapshot(id);

        string second = await Post(id, "advance", stamp);

        Assert.Equal(CyclePhase.Briefing, Snapshot(id).Phase);
        Assert.Equal(before, Snapshot(id));
        AssertRefreshed(second, CyclePhase.Briefing);
        Assert.Contains("<form id=\"briefing\"", Patches(second)[1]);
    }

    [Fact]
    public async Task A_double_click_on_to_aftermath_stays_in_aftermath()
    {
        Guid id = await ResolutionWithNobodySent();
        List<KeyValuePair<string, string>> stamp = app.Stamp(id);
        await Post(id, "advance", stamp);
        var before = Snapshot(id);

        string second = await Post(id, "advance", stamp);

        Assert.Equal(CyclePhase.Aftermath, Snapshot(id).Phase);
        Assert.Equal(before, Snapshot(id));
        AssertRefreshed(second, CyclePhase.Aftermath);
        Assert.Contains("Next cycle", Patches(second)[1]);
    }

    [Fact]
    public async Task A_double_click_on_next_cycle_advances_one_cycle_not_two()
    {
        Guid id = await ResolutionWithNobodySent();
        await Post(id, "advance", app.Stamp(id));
        List<KeyValuePair<string, string>> stamp = app.Stamp(id);
        await Post(id, "advance", stamp);
        var before = Snapshot(id);

        string second = await Post(id, "advance", stamp);

        Assert.Equal((CyclePhase.Orders, 2), (Snapshot(id).Phase, Snapshot(id).Cycle));
        Assert.Equal(before, Snapshot(id));
        AssertRefreshed(second, CyclePhase.Orders);
    }

    [Theory]
    [InlineData("Orders", 0)]
    [InlineData("Briefing", 1)]
    public async Task A_launch_posted_from_a_stale_page_stores_nothing(string phase, int cycleOffset)
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        await Post(id, "advance", app.Stamp(id));
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        var team = (AssignTeam)SimRunner.AutoCaptain.NextCommand(app.Live.Game, legacy.State, app.Live.Pack);
        var form = new List<KeyValuePair<string, string>>
        {
            new("objective", team.ObjectiveId), new("lead", team.LeadId), new("priority", "0"), new("risk", "0"), new("autonomy", "0"),
            new("cycle", (legacy.State.Cycle + cycleOffset).ToString(System.Globalization.CultureInfo.InvariantCulture)), new("phase", phase),
        };
        form.AddRange(team.CrewIds.Select(c => new KeyValuePair<string, string>("crew", c)));
        var before = Snapshot(id);

        string stream = await Post(id, "launch", form);

        Assert.Equal(before, Snapshot(id));
        AssertRefreshed(stream, CyclePhase.Briefing);
    }

    [Fact]
    public async Task An_answer_posted_from_a_page_in_another_phase_stores_nothing()
    {
        Guid id = await ResolutionWithNobodySent();
        await Post(id, "advance", app.Stamp(id));
        var before = Snapshot(id);

        string stream = await Post(id, "answer", [new("mission", "m"), new("call", "c"), new("answer", "Bold"), new("cycle", "1"), new("phase", "Resolution")]);

        Assert.Equal(before, Snapshot(id));
        AssertRefreshed(stream, CyclePhase.Aftermath);
    }

    public static TheoryData<string, string?, string?> BadStamps => new()
    {
        { "advance", null, "Orders" },
        { "advance", "1", null },
        { "advance", "one", "Orders" },
        { "advance", "1", "Sideways" },
        // A number would parse as an enum value.
        { "advance", "1", "0" },
        { "launch", null, "Orders" },
        { "answer", "1", null },
    };

    [Theory]
    [MemberData(nameof(BadStamps))]
    public async Task A_missing_or_unparseable_cycle_or_phase_is_an_incomplete_form_and_stores_nothing(string route, string? cycle, string? phase)
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        var form = new List<KeyValuePair<string, string>>();
        if (cycle is not null)
        {
            form.Add(new("cycle", cycle));
        }

        if (phase is not null)
        {
            form.Add(new("phase", phase));
        }

        var before = Snapshot(id);

        string stream = await Post(id, route, form);

        Assert.Contains("The form was not complete.", stream);
        Assert.Equal(before, Snapshot(id));
    }

    private static void AssertStamped(string form, int cycle, CyclePhase phase)
    {
        Assert.Contains($"<input type=\"hidden\" name=\"cycle\" value=\"{cycle}\" />", form);
        Assert.Contains($"<input type=\"hidden\" name=\"phase\" value=\"{phase}\" />", form);
    }

    private static string FormAround(string html, string label)
    {
        int at = html.IndexOf(label, StringComparison.Ordinal);
        Assert.True(at >= 0, $"No {label} on the page.");
        int start = html.LastIndexOf("<form", at, StringComparison.Ordinal);
        return html[start..(html.IndexOf("</form>", at, StringComparison.Ordinal) + 7)];
    }

    private static void AssertDisabledInFlight(string html, string label)
    {
        Match button = Regex.Match(html, $"<button[^>]*>{Regex.Escape(label)}</button>");
        Assert.True(button.Success, $"No {label} button.");
        Assert.Matches("data-attr:disabled=\"\\$_[a-z0-9_]+", button.Value);
    }

    [Fact]
    public async Task Every_advance_button_posts_the_pages_cycle_and_phase_and_is_disabled_in_flight()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        string orders = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));
        AssertStamped(FormAround(orders, "To briefing"), 1, CyclePhase.Orders);
        Assert.Contains($"data-on:submit=\"@post('/legacy/{id}/advance', {{contentType: 'form'}})\"", FormAround(orders, "To briefing"));
        AssertDisabledInFlight(orders, "To briefing");

        await Post(id, "advance", app.Stamp(id));
        await Post(id, "advance", app.Stamp(id));
        string resolution = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));
        AssertStamped(FormAround(resolution, "To aftermath"), 1, CyclePhase.Resolution);
        AssertDisabledInFlight(resolution, "To aftermath");

        await Post(id, "advance", app.Stamp(id));
        string aftermath = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));
        AssertStamped(FormAround(aftermath, "Next cycle"), 1, CyclePhase.Aftermath);
        AssertDisabledInFlight(aftermath, "Next cycle");
    }

    [Fact]
    public async Task The_briefing_posts_its_stamp_with_launch_and_send_nobody_and_disables_both_while_either_is_in_flight()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        await Post(id, "advance", app.Stamp(id));

        string html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));

        string briefing = Regex.Match(html, "<form id=\"briefing\".*?</form>", RegexOptions.Singleline).Value;
        AssertStamped(briefing, 1, CyclePhase.Briefing);
        AssertStamped(Regex.Match(html, "<form id=\"send-nobody\".*?</form>", RegexOptions.Singleline).Value, 1, CyclePhase.Briefing);
        Assert.Contains($"@post('/legacy/{id}/advance', {{contentType: 'form', selector: '#send-nobody'}})", html);
        foreach (string label in new[] { "Launch", "Send nobody" })
        {
            Match button = Regex.Match(html, $"<button[^>]*>{label}</button>");
            Assert.Contains("data-attr:disabled=\"$_launching || $_sending\"", button.Value);
        }
    }

    [Fact]
    public async Task Every_answer_form_posts_its_stamp_and_every_answer_button_is_disabled_while_any_answer_is_in_flight()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.Sessions.CreateLegacy(app.Live, $"LEGACY_{seed}", seed);
            var captain = new WebCaptain(app, client, id);
            for (int steps = 0; steps < 50 && (await captain.Read()).State.Phase != CyclePhase.Resolution; steps++)
            {
                await captain.Step();
            }

            LoadedLegacy legacy = await captain.Read();
            if (!legacy.State.TryModule(MissionsModule.Name, out MissionsState? missions) || missions.Site?.Mission?.State.Pending is not { } pending)
            {
                continue;
            }

            string html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));

            MatchCollection forms = Regex.Matches(html, "<form data-on:submit=\"@post\\('/legacy/[^']+/answer'.*?</form>", RegexOptions.Singleline);
            Assert.Equal(pending.Options.Count, forms.Count);
            string all = string.Join(" || ", Enumerable.Range(0, forms.Count).Select(i => $"$_answer{i}"));
            Assert.All(forms, f => AssertStamped(f.Value, legacy.State.Cycle, CyclePhase.Resolution));
            Assert.All(forms, f => Assert.Contains($"data-attr:disabled=\"{all}\"", f.Value));
            return;
        }

        Assert.Fail("No seed to 200 left a call waiting.");
    }
}

using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Crew;
using Web.Bridge;

namespace Web.Tests;

// What the calendar puts on screen (slice 1e-calendar, Task 5; design 4.7 and 4.9): play shows dates and weeks left, never a week
// number, a day number, or the cycle; Dev Play shows the numbers and travels.
public sealed partial class CalendarScreensTests
{
    [GeneratedRegex(@"<(script|style)\b.*?</\1>", RegexOptions.Singleline)]
    private static partial Regex Code();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();

    // The words a player can read: no markup, no attribute (a form's hidden stamp carries the cycle, and is never shown).
    private static string Visible(string html)
    {
        return Regex.Replace(WebUtility.HtmlDecode(Tag().Replace(Code().Replace(html, " "), " ")), @"\s+", " ");
    }

    // Review focus 5: "Week " and a figure, "Cycle", a day number, or any figure of four digits or more (but a calendar date's
    // year, as the legacies list's "Started 2026-11-20").
    private static void NoNumbersEscape(string html, int day, string where)
    {
        string text = Visible(html);
        Assert.False(Regex.IsMatch(text, @"\bWeek \d"), $"{where} shows a week number: {text}");
        Assert.False(text.Contains("Cycle", StringComparison.Ordinal), $"{where} shows the cycle: {text}");
        Assert.False(Regex.IsMatch(text, @"(?<![\w.\-])\d{4,}(?![\w\-])"), $"{where} shows a bare day number: {text}");
        Assert.DoesNotContain(day.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
    }

    [Fact]
    public async Task No_play_screen_shows_a_week_number_a_day_number_or_the_cycle()
    {
        using var app = new TestApp();
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 3);
        // Light injuries and a recovering member, so the roster shows time left from the first page.
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            CrewState crew = legacy.State.Module<CrewState>(CrewModule.Name);
            int today = legacy.State.Day;
            crew.Members[0].LightInjuriesHealAtDay.AddRange([today + 9, today + 20]);
            crew.Members[1].Status = CrewStatus.Recovering;
            crew.Members[1].ReturnsAtDay = today + 40;
            return legacy;
        });
        var captain = new WebCaptain(app, client, id);
        var phases = new HashSet<CyclePhase>();
        var injured = new HashSet<string>();

        NoNumbersEscape(await client.GetStringAsync("/"), (await captain.Read()).State.Day, "The legacies list");
        string first = await client.GetStringAsync($"/legacy/{id}");
        for (int steps = 0; steps < 400 && (await captain.Read()).State.Cycle < 4; steps++)
        {
            LegacyState state = (await captain.Read()).State;
            string page = await client.GetStringAsync($"/legacy/{id}");
            NoNumbersEscape(page, state.Day, $"The {state.Phase} screen of cycle {state.Cycle}");
            // The Chronicle (1e part five, Task 6) opens in any phase, and is held to the same rule.
            NoNumbersEscape(await client.GetStringAsync($"/legacy/{id}/chronicle"), state.Day, $"The Chronicle in the {state.Phase} of cycle {state.Cycle}");
            NoNumbersEscape(await client.GetStringAsync($"/legacy/{id}/chronicle?show=notable"), state.Day, $"The Chronicle's notable entries in the {state.Phase} of cycle {state.Cycle}");
            phases.Add(state.Phase);
            injured.UnionWith(Regex.Matches(Visible(page), @"(heals|returns) in (under a week|1 week|\d+ weeks)").Select(m => m.Value));
            await captain.Step();
        }

        // 9 and 20 days from the start: 2 weeks (nearest) and 3 weeks, as the first page shows them; 40 days, 7 weeks.
        Assert.Contains("2 light injuries (heals in 2 weeks and 3 weeks)", Visible(first));
        Assert.Contains("returns in 7 weeks", injured);
        Assert.Superset(new HashSet<CyclePhase> { CyclePhase.Itinerary, CyclePhase.Orders, CyclePhase.Briefing, CyclePhase.Resolution, CyclePhase.Aftermath }, phases);
    }

    [Fact]
    public async Task The_top_bar_shows_the_short_date_with_the_full_date_on_hover()
    {
        using var app = new TestApp();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);

        string page = await app.NewClient().GetStringAsync($"/legacy/{id}");

        // The fixture's calendar starts on 13 MONTH_B 105, a WEEKDAY_A (WD_A), as the game's starts on Stoneday, 13 Vortun 105.
        Assert.Contains("<div title=\"WEEKDAY_A, 13 MONTH_B 105\">", page);
        Assert.Contains("<span aria-hidden=\"true\">13 MONTH_B 105</span><span class=\"visually-hidden\">WEEKDAY_A, 13 MONTH_B 105</span>", page);
        Assert.Contains("<span class=\"clock__weekday\" aria-hidden=\"true\">WD_A</span>", page);
    }

    [Fact]
    public async Task The_legacies_list_shows_each_legacys_date_and_dev_mode_its_cycle()
    {
        using var play = new TestApp();
        await play.CreateAtOrders("LEGACY_ONE", 1);
        using var dev = new TestApp(developerMode: true, scratch: true);
        await dev.CreateAtOrders("LEGACY_ONE", 1);

        string list = await play.NewClient().GetStringAsync("/");
        string devList = await dev.NewClient().GetStringAsync("/");

        Assert.Contains("<div><dt>Date</dt><dd class=\"m\" title=\"WEEKDAY_A, 13 MONTH_B 105\">13 MONTH_B 105</dd></div>", list);
        Assert.DoesNotContain("<dt>Cycle</dt>", list);
        Assert.Contains("<div><dt>Cycle</dt><dd class=\"m\">1</dd></div>", devList);
    }

    [Fact]
    public async Task Dev_play_shows_the_cycle_the_day_number_and_a_traits_change_as_a_date()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);
        HttpResponseMessage set = await client.PostAsync($"/dev/legacy/{id}/trait", new FormUrlEncodedContent([new("trait", "trait_a"), new("on", "ship"), new("value", "1")]));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        string page = WebUtility.HtmlDecode(await client.GetStringAsync($"/dev/legacy/{id}"));

        Assert.Contains("<div class=\"clock__dev\"><dt>Cycle</dt><dd>1</dd></div>", page);
        Assert.Contains("<div><dt>Cycle</dt><dd class=\"m\">1</dd></div>", page);
        Assert.Contains("<div><dt>Day</dt><dd class=\"m\">37482</dd></div>", page);
        Assert.Contains("<div><dt>Date</dt><dd>WEEKDAY_A, 13 MONTH_B 105</dd></div>", page);
        Assert.Contains("<th scope=\"col\">Changed</th>", page);
        Assert.Contains("<td class=\"m\" title=\"Day 37482\">13 MONTH_B 105</td>", page);
    }

    [Fact]
    public async Task Dev_play_travels_n_weeks_from_the_aftermath_in_one_turn()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);
        var captain = new WebCaptain(app, client, id);
        while ((await captain.Read()).State.Phase != CyclePhase.Aftermath)
        {
            await captain.Step();
        }

        string aftermath = WebUtility.HtmlDecode(await client.GetStringAsync($"/dev/legacy/{id}"));
        string play = await client.GetStringAsync($"/legacy/{id}");
        int before = (await captain.Read()).State.Day;
        HttpResponseMessage response = await client.PostAsync($"/dev/legacy/{id}/travel", new FormUrlEncodedContent(app.Stamped(id, [new("weeks", "5")])));
        string stream = await response.Content.ReadAsStringAsync();
        LegacyState after = (await captain.Read()).State;

        Assert.Contains("<form class=\"travel\"", aftermath);
        Assert.Contains($"@post('/dev/legacy/{id}/travel', {{contentType: 'form'}})", aftermath);
        Assert.DoesNotContain("class=\"travel\"", play);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((CyclePhase.Itinerary, 2), (after.Phase, after.Cycle));
        Assert.Contains(after.Day - before, new[] { 25, 30, 35 });
        Assert.Contains($"<span aria-hidden=\"true\">{Dates.Short(app.Live.Pack.Calendar, after.Day)}</span>", stream);
    }

    [Theory]
    [InlineData("0", "A journey is 1 to 10 weeks.")]
    [InlineData("11", "A journey is 1 to 10 weeks.")]
    [InlineData("five", "A journey is 1 to 10 weeks.")]
    public async Task Dev_play_refuses_a_journey_out_of_range_and_changes_nothing(string weeks, string reason)
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);
        var captain = new WebCaptain(app, client, id);
        while ((await captain.Read()).State.Phase != CyclePhase.Aftermath)
        {
            await captain.Step();
        }

        LegacyState before = (await captain.Read()).State;
        HttpResponseMessage response = await client.PostAsync($"/dev/legacy/{id}/travel", new FormUrlEncodedContent(app.Stamped(id, [new("weeks", weeks)])));
        LegacyState after = (await captain.Read()).State;

        Assert.Contains(reason, await response.Content.ReadAsStringAsync());
        Assert.Equal((before.Phase, before.Cycle, before.Day), (after.Phase, after.Cycle, after.Day));
    }

    [Fact]
    public async Task Travel_is_refused_outside_the_aftermath_and_does_not_exist_in_play()
    {
        using var dev = new TestApp(developerMode: true, scratch: true);
        Guid id = await dev.CreateAtOrders("LEGACY_ONE", 1);
        HttpResponseMessage orders = await dev.NewClient().PostAsync($"/dev/legacy/{id}/travel", new FormUrlEncodedContent(dev.Stamped(id, [new("weeks", "2")])));
        using var play = new TestApp();
        Guid other = await play.CreateAtOrders("LEGACY_ONE", 1);
        HttpResponseMessage absent = await play.NewClient().PostAsync($"/legacy/{other}/travel", new FormUrlEncodedContent(play.Stamped(other, [new("weeks", "2")])));

        Assert.NotEqual(HttpStatusCode.OK, orders.StatusCode);
        Assert.Equal(CyclePhase.Orders, (await dev.Sessions.Read(dev.Live, id))!.State.Phase);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
    }
}

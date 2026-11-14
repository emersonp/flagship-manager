using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;

namespace Web.Tests;

// The real pages and streams with a pack whose every name is markup and digits: names are text, ids stay keys.
public sealed class PageNamesTests : IDisposable
{
    private const string Encoded = "NAME 7 &lt;b&gt; &amp; -3";

    private readonly TestApp app = new(oddNames: true);
    private readonly HttpClient client;

    public PageNamesTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    [Fact]
    public async Task The_roster_orders_and_briefing_show_encoded_names_and_keep_ids_in_field_values_and_elements()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);
        var captain = new WebCaptain(app, client, id);
        string orders = await client.GetStringAsync($"/legacy/{id}");
        await captain.Step();
        string briefing = await client.GetStringAsync($"/legacy/{id}");

        Assert.Contains($"<td class=\"who\">{Encoded} CREW_01</td>", orders);
        Assert.Contains($"<h1>{Encoded} ASSIGNMENT_TEMPLATE_1</h1>", orders);
        Assert.Contains("<tr data-crew=\"CREW_01\"", orders);
        Assert.DoesNotContain("<b>", orders);

        Assert.Contains("name=\"crew\" value=\"CREW_01\"", briefing);
        Assert.Contains("id=\"crew-CREW_01\"", briefing);
        Assert.Contains("name=\"gear\" value=\"GEAR_01\"", briefing);
        Assert.Contains($"<span class=\"gear__id\">{Encoded} GEAR_01</span>", briefing);
        Assert.Contains($"aria-label=\"Team {Encoded} CREW_01\"", briefing);
        Assert.Matches("name=\"objective\" value=\"o_", briefing);
        Assert.DoesNotContain("<b>", briefing);
    }

    [Fact]
    public async Task A_launch_streams_encoded_names_in_the_log_the_route_and_the_head()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_TWO", 1);
        var captain = new WebCaptain(app, client, id);
        string stream = "";
        for (int steps = 0; steps < 50 && stream.Length == 0; steps++)
        {
            (string route, string body) = await captain.Step();
            stream = route == "launch" ? body : "";
        }

        Assert.NotEmpty(stream);
        string decoded = WebUtility.HtmlDecode(stream);
        Assert.Contains(Encoded, stream);
        Assert.DoesNotContain("<b>", stream);
        Assert.Matches($"class=\"stop__id\">{Regex.Escape(Encoded)} ENCOUNTER_", stream);
        Assert.Contains("<span class=\"m\">", decoded);
        Assert.DoesNotContain("<span class=\"m\">7", decoded);
    }
}

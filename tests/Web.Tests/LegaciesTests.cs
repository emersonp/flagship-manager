using System.Net;
using Persistence;

namespace Web.Tests;

public sealed class LegaciesTests : IDisposable
{
    private readonly TestApp app = new();

    public void Dispose()
    {
        app.Dispose();
    }

    private static FormUrlEncodedContent Form(string name)
    {
        return new FormUrlEncodedContent([new KeyValuePair<string, string>("name", name)]);
    }

    [Fact]
    public async Task The_empty_list_page_shows_the_empty_state_and_the_form()
    {
        HttpResponseMessage response = await app.NewClient().GetAsync("/");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"empty\"", html);
        Assert.Contains("<form method=\"post\" action=\"/legacies\"", html);
        Assert.Contains("/lib/datastar.js", html);
    }

    [Fact]
    public async Task Creating_a_legacy_redirects_to_its_bridge_and_the_list_then_shows_it()
    {
        HttpClient client = app.NewClient();

        HttpResponseMessage created = await client.PostAsync("/legacies", Form("  LEGACY_ONE  "));

        Assert.Equal(HttpStatusCode.SeeOther, created.StatusCode);
        string location = created.Headers.Location!.ToString();
        Assert.StartsWith("/legacy/", location);
        string list = await client.GetStringAsync("/");
        Assert.Contains("LEGACY_ONE", list);
        Assert.Contains(location, list);
        Assert.DoesNotContain("class=\"empty\"", list);
    }

    [Theory]
    [InlineData("   ", "Enter a name.")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890", "Use 60 characters or fewer.")]
    public async Task A_bad_name_is_refused_with_the_reason_and_nothing_is_created(string name, string reason)
    {
        HttpClient client = app.NewClient();

        HttpResponseMessage response = await client.PostAsync("/legacies", Form(name));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, html);
        Assert.Empty(app.Game.Store.ListLegacies());
    }

    [Theory]
    [InlineData("/legacy/00000000-0000-0000-0000-000000000001")]
    [InlineData("/legacy/not-a-guid")]
    public async Task An_unknown_or_malformed_id_is_a_404_page(string url)
    {
        HttpResponseMessage response = await app.NewClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<main id=\"main\">", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_bridge_shows_week_1_cycle_1_and_the_orders_phase()
    {
        HttpClient client = app.NewClient();
        HttpResponseMessage created = await client.PostAsync("/legacies", Form("LEGACY_ONE"));

        string html = await client.GetStringAsync(created.Headers.Location);

        Assert.Contains("<header id=\"topbar\">", html);
        Assert.Contains("<aside id=\"roster\">", html);
        Assert.Contains("LEGACY_ONE", html);
        Assert.Contains("Week 1", html);
        Assert.Contains("Cycle 1", html);
        Assert.Contains("Orders", html);
    }
}

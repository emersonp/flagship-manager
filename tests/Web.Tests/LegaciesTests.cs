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
    [InlineData("   ", "A legacy name is 1 to 60 characters.")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890", "A legacy name is 1 to 60 characters.")]
    public async Task A_bad_name_is_refused_with_the_reason_and_nothing_is_created(string name, string reason)
    {
        HttpClient client = app.NewClient();

        HttpResponseMessage response = await client.PostAsync("/legacies", Form(name));
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, html);
        Assert.Empty(app.Game.Store.ListLegacies());
    }

    public static TheoryData<string> UnreadableForms => new() { "no body", "no content type", "over-large" };

    // A body that is not a readable form is a refusal on the page, not a failure.
    [Theory]
    [MemberData(nameof(UnreadableForms))]
    public async Task A_post_that_is_not_a_readable_form_is_refused_with_the_page_and_a_reason(string kind)
    {
        HttpContent? content = kind switch
        {
            "no body" => null,
            "no content type" => new StringContent("name=LEGACY_ONE"),
            _ => Form(new string('A', 5 * 1024 * 1024)),
        };
        if (kind == "no content type")
        {
            content!.Headers.ContentType = null;
        }

        HttpResponseMessage response = await app.NewClient().PostAsync("/legacies", content);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("<form method=\"post\" action=\"/legacies\"", html);
        Assert.Contains("<p class=\"error\" id=\"name-error\">The form was not complete.</p>", html);
        Assert.Empty(app.Game.Store.ListLegacies());
    }

    [Fact]
    public async Task The_name_field_holds_at_most_sixty_characters()
    {
        string html = await app.NewClient().GetStringAsync("/");

        Assert.Matches("<input id=\"name\" name=\"name\"[^>]* maxlength=\"60\"", html);
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
        Assert.Contains("<dt>Week</dt><dd>1</dd>", html);
        Assert.Contains("<dt>Cycle</dt><dd>1</dd>", html);
        Assert.Contains("Orders", html);
    }

    [Fact]
    public async Task A_name_with_markup_is_shown_as_text_on_the_list_and_the_bridge()
    {
        HttpClient client = app.NewClient();
        HttpResponseMessage created = await client.PostAsync("/legacies", Form("<script>x</script>"));

        string list = await client.GetStringAsync("/");
        string bridge = await client.GetStringAsync(created.Headers.Location);

        foreach (string html in new[] { list, bridge })
        {
            Assert.Contains("&lt;script&gt;x&lt;/script&gt;", html);
            Assert.DoesNotContain("<script>x</script>", html);
        }
    }
}

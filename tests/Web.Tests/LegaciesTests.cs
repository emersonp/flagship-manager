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
    public async Task The_bridge_shows_the_start_day_cycle_1_and_the_orders_phase()
    {
        HttpClient client = app.NewClient();
        HttpResponseMessage created = await client.PostAsync("/legacies", Form("LEGACY_ONE"));

        string html = await client.GetStringAsync(created.Headers.Location);

        Assert.Contains("<header id=\"topbar\">", html);
        Assert.Contains("<aside id=\"roster\">", html);
        Assert.Contains("LEGACY_ONE", html);
        Assert.Contains("<dt>Day</dt><dd>37482</dd>", html);
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

    [Fact]
    public async Task Each_legacy_has_a_delete_that_asks_first_and_deleting_removes_it_whole()
    {
        HttpClient client = app.NewClient();
        Guid kept = await app.Sessions.CreateLegacy(app.Live, "LEGACY_KEPT", 1);
        Guid gone = await app.Sessions.CreateLegacy(app.Live, "LEGACY_GONE", 2);

        string list = await client.GetStringAsync("/");
        HttpResponseMessage deleted = await client.PostAsync($"/legacies/{gone}/delete", null);
        string after = await client.GetStringAsync("/");

        Assert.Contains($"<form method=\"post\" action=\"/legacies/{gone}/delete\"", list);
        Assert.Contains("Delete LEGACY_GONE for good?", list);
        Assert.Equal(HttpStatusCode.SeeOther, deleted.StatusCode);
        Assert.Equal("/", deleted.Headers.Location!.ToString());
        Assert.DoesNotContain("LEGACY_GONE", after);
        Assert.Contains("LEGACY_KEPT", after);
        Assert.Null(app.Game.Store.Load(gone, app.Live.Game.ModuleTypes));
        Assert.Empty(app.Game.Store.ReadEvents(gone));
        Assert.NotEmpty(app.Game.Store.ReadEvents(kept));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/legacy/{gone}")).StatusCode);
        // The kept legacy's lock stays; the deleted one's is gone.
        Assert.Equal(1, app.Sessions.LockCount);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public async Task Deleting_a_legacy_that_is_not_there_goes_back_to_the_list(string id)
    {
        Guid kept = await app.Sessions.CreateLegacy(app.Live, "LEGACY_KEPT", 1);

        HttpResponseMessage response = await app.NewClient().PostAsync($"/legacies/{id}/delete", null);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.ToString());
        Assert.Single(app.Game.Store.ListLegacies(), l => l.LegacyId == kept);
    }
}

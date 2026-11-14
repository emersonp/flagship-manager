using System.Net;
using System.Text.RegularExpressions;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// The three text file pages (the two tuning files and the pack file), through the real app on a scratch copy of the content.
public sealed class ContentTextFilesTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    public static TheoryData<string, string> Files => new()
    {
        { "missions", "tuning/missions.yaml" },
        { "crew-tuning", "tuning/crew.yaml" },
        { "pack", "pack.yaml" },
    };

    // The text area's text as the browser sees it: decoded. A literal line break straight after the tag would be dropped, so none may be there.
    private static string Typed(string body)
    {
        string raw = Regex.Match(body, "<textarea\\b[^>]*>(.*?)</textarea>", RegexOptions.Singleline).Groups[1].Value;
        Assert.False(raw.StartsWith('\n'), "a literal line break after the tag is dropped by the browser");
        return WebUtility.HtmlDecode(raw);
    }

    private static string Body(string text) => "text=" + Uri.EscapeDataString(text);

    [Theory]
    [MemberData(nameof(Files))]
    public async Task A_page_shows_the_files_text_in_a_labelled_text_area_with_a_save_button(string name, string path)
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Get(app.NewClient(), $"/dev/content/files/{name}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Disk(app, path), Typed(body));
        Assert.Matches("<label for=\"text\">[^<]*" + Regex.Escape(path), body);
        Assert.Matches("<textarea id=\"text\" name=\"text\"[^>]*wrap=\"off\"", body);
        Assert.Matches("<button[^>]*type=\"submit\"[^>]*data-attr:disabled=\"\\$_saving\"[^>]*>Save</button>", body);
        Assert.Contains("id=\"form-alert\"", body);
    }

    // A text area drops a literal line break straight after its tag, so a file that starts with one must reach it as a character reference.
    [Fact]
    public async Task A_file_that_starts_with_a_line_break_is_shown_whole()
    {
        using TestApp app = NewApp();
        await File.WriteAllTextAsync(Path.Combine(app.ContentPath, "tuning", "crew.yaml"), "\n" + Disk(app, "tuning/crew.yaml"));
        _ = await app.Editor.Reload();

        (_, string body) = await Get(app.NewClient(), "/dev/content/files/crew-tuning");

        Assert.StartsWith("\n# Every number the crew module uses", Typed(body));
    }

    [Theory]
    [InlineData("missions")]
    [InlineData("crew-tuning")]
    public async Task The_tuning_pages_say_the_number_page_comes_later_and_a_mission_in_flight_keeps_its_tuning(string name)
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), $"/dev/content/files/{name}");

        string text = WebUtility.HtmlDecode(body);
        Assert.Contains("The page that edits these numbers and re-runs the bench comes later.", text);
        Assert.Contains("A mission in flight keeps the tuning it started with.", text);
    }

    [Fact]
    public async Task The_pack_page_says_the_id_is_fixed_and_a_new_version_warns_existing_legacies()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content/files/pack");

        string text = WebUtility.HtmlDecode(body);
        Assert.Contains("The pack's id cannot be changed here.", text);
        Assert.Contains("Changing the version makes every existing legacy show a warning that its content has changed.", text);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("skills")]
    [InlineData("Missions")]
    [InlineData("tuning")]
    [InlineData("..%2Fpack")]
    public async Task Any_other_name_is_404_for_the_page_and_the_save(string name)
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, $"/dev/content/files/{name}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, $"/dev/content/files/{name}", Body("id: X\n"))).Status);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task With_developer_mode_off_the_text_file_routes_are_404()
    {
        using var app = new TestApp();
        HttpClient client = app.NewClient();

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/content/files/pack")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/dev/content/files/pack", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task A_save_writes_exactly_the_typed_text_and_the_game_uses_it_at_once()
    {
        using TestApp app = NewApp();
        string typed = Disk(app, "tuning/missions.yaml").Replace("  gearBonus: 2\n", "  gearBonus: 3\n", StringComparison.Ordinal);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/missions", Body(typed));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(typed, Disk(app, "tuning/missions.yaml"));
        Assert.Equal(3, app.Live.Pack.Tuning.GearBonus);
        Assert.Contains("Saved.", body);
        Assert.Equal(typed, Typed(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\n\n\n")]
    public async Task The_trailing_line_breaks_are_kept_as_typed(string tail)
    {
        using TestApp app = NewApp();
        string typed = Disk(app, "tuning/crew.yaml").TrimEnd('\n') + tail;

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/files/crew-tuning", Body(typed));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(typed, Disk(app, "tuning/crew.yaml"));
    }

    // Browsers post a text area's line breaks as CRLF; a file written with LF stays LF.
    [Fact]
    public async Task A_browsers_crlf_is_saved_as_the_files_own_line_ending_lf()
    {
        using TestApp app = NewApp();
        string typed = Disk(app, "pack.yaml").Replace("version: 1", "version: 2", StringComparison.Ordinal);

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/files/pack", Body(typed.Replace("\n", "\r\n", StringComparison.Ordinal)));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(typed, Disk(app, "pack.yaml"));
        Assert.DoesNotContain('\r', Disk(app, "pack.yaml"));
        Assert.Equal(2, app.Live.Pack.Version);
    }

    // A file already written with CRLF keeps it, so a save is not a whole-file diff.
    [Fact]
    public async Task A_file_written_with_crlf_keeps_crlf()
    {
        using TestApp app = NewApp(root =>
        {
            string file = Path.Combine(root, "pack.yaml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("\n", "\r\n", StringComparison.Ordinal));
        });
        string typed = "id: CORE\r\nversion: 2\r\n";

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/files/pack", Body(typed));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(typed, Disk(app, "pack.yaml"));
    }

    [Fact]
    public async Task A_save_the_loader_refuses_shows_its_errors_keeps_the_typed_text_and_writes_nothing()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);
        var live = app.Live;
        string typed = Disk(app, "tuning/missions.yaml").Replace("  gearBonus: 2\n", "  gearBonis: 2\n", StringComparison.Ordinal);

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/files/missions", Body(typed));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(typed, Typed(body));
        Assert.Matches("<div id=\"form-alert\"[^>]*>.*check\\.gearBonus is missing\\.", Regex.Replace(body, "\\s+", " "));
        Assert.Contains("Nothing was written.", body);
        Assert.Matches("id=\"form-alert\"[^>]*data-init=\"el\\.focus\\(\\)", body);
        Assert.Equal(before, Everything(app));
        Assert.Same(live, app.Live);
    }

    [Fact]
    public async Task An_empty_text_is_refused_and_nothing_is_written()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/files/pack", Body(""));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_post_with_no_text_field_is_refused_and_nothing_is_written()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/files/pack", "");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_change_to_the_packs_id_is_refused_with_its_own_reason()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/pack", Body("id: OTHER\nversion: 1\n"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("The pack's id cannot be changed here. It is CORE.", WebUtility.HtmlDecode(body));
        Assert.Equal("id: OTHER\nversion: 1\n", Typed(body));
        Assert.Equal(before, Everything(app));
        Assert.Equal("CORE", app.Live.Pack.Id);
    }

    [Fact]
    public async Task A_new_version_saves_and_the_index_shows_it()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode status, _) = await Post(client, "/dev/content/files/pack", Body("id: CORE\nversion: 7\n"));
        (_, string index) = await Get(client, "/dev/content");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("id: CORE\nversion: 7\n", Disk(app, "pack.yaml"));
        Assert.Matches("version </span><span class=\"m\">7</span>", index);
    }

    [Fact]
    public async Task A_datastar_save_patches_the_page_with_the_notice_and_a_refusal_keeps_the_text()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string typed = Disk(app, "tuning/missions.yaml");

        (HttpStatusCode saved, string stream) = await Post(client, "/dev/content/files/missions", Body(typed), datastar: true);
        (HttpStatusCode refused, string refusal) = await Post(client, "/dev/content/files/missions", Body("check: ["), datastar: true);

        Assert.Equal(HttpStatusCode.OK, saved);
        Assert.Contains("event: datastar-patch-elements", stream);
        Assert.Contains("Saved.", stream);
        Assert.Equal(HttpStatusCode.OK, refused);
        Assert.Contains("Nothing was written.", refusal);
        Assert.Contains("check: [", WebUtility.HtmlDecode(refusal));
        Assert.Equal(typed, Disk(app, "tuning/missions.yaml"));
    }

    [Fact]
    public async Task A_cross_origin_post_is_refused_and_writes_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/files/pack", Body("id: CORE\nversion: 9\n"), headers: h => h.Add("Sec-Fetch-Site", "cross-site"));

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task The_index_links_each_file_to_its_page()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content");

        foreach ((string name, string path) in new[] { ("pack", "pack.yaml"), ("missions", "tuning/missions.yaml"), ("crew-tuning", "tuning/crew.yaml") })
        {
            Assert.Contains($"<a href=\"/dev/content/files/{name}\">{path}</a>", body);
        }
    }

    [Fact]
    public async Task The_repositorys_own_content_is_byte_identical_after_text_saves()
    {
        using TestApp app = NewApp();

        await Post(app.NewClient(), "/dev/content/files/pack", Body("id: CORE\nversion: 5\n"));

        guard.Verify();
    }
}

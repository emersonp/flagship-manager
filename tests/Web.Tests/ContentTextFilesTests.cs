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
        { "calendar-tuning", "tuning/calendar.yaml" },
        { "chronicle-tuning", "tuning/chronicle.yaml" },
        { "calendars", "calendars/base.yaml" },
        { "pack", "pack.yaml" },
        { "storylets", "storylets/base.yaml" },
        { "vignettes", "vignettes/base.yaml" },
        { "testships", "testships/base.yaml" },
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

    private const int Limit = 2 * 1024 * 1024;

    // A pack file of exactly this many bytes of UTF-8: valid, with the rest a comment.
    private static string PackOfSize(int bytes)
    {
        const string head = "id: CORE\nversion: 2\ncalendar: calendar_a\n# ";
        return head + new string('x', bytes - head.Length - 1) + "\n";
    }

    // The limit is 2 MB of text: exactly that saves, one byte more is answered plainly with 413 and nothing is written.
    [Fact]
    public async Task A_text_at_the_limit_saves_and_one_byte_more_is_too_large()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);

        (HttpStatusCode over, string refused) = await Post(client, "/dev/content/files/pack", Body(PackOfSize(Limit + 1)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, over);
        Assert.Equal("That is too large to save here (2 MB).", refused);
        Assert.Equal(before, Everything(app));

        (HttpStatusCode at, _) = await Post(client, "/dev/content/files/pack", Body(PackOfSize(Limit)));
        Assert.Equal(HttpStatusCode.OK, at);
        Assert.Equal(Limit, new FileInfo(Path.Combine(app.ContentPath, "pack.yaml")).Length);
    }

    // From the page, only the alert is patched: the text area keeps what was typed, since the text is never echoed.
    [Fact]
    public async Task A_text_too_large_from_the_page_patches_only_the_alert()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/missions", Body(PackOfSize(Limit + 1)), datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("data: elements <div id=\"form-alert\"", body);
        Assert.Contains("That is too large to save here (2 MB).", body);
        Assert.DoesNotContain("textarea", body);
        Assert.DoesNotContain("xxxx", body);
        Assert.Equal(before, Everything(app));
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
        string typed = "id: CORE\r\nversion: 2\r\ncalendar: calendar_a\r\n";

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

    // 1e part four, design 4.12: an expression error in the storylets file is shown where it is in the text, by line and column, and
    // with that line marked under the character, beside the loader's own words ("at character n" of the expression).
    [Fact]
    public async Task An_expression_error_shows_its_position_in_the_editor()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);
        string typed = Disk(app, "storylets/base.yaml").Replace("subject.trait.trait_c == 0", "subject.trait.nosuch == 0", StringComparison.Ordinal);
        int at = typed.IndexOf("nosuch", StringComparison.Ordinal);
        int line = typed[..at].Count(c => c == '\n') + 1;
        int column = at - typed.LastIndexOf('\n', at);
        string text = typed.Split('\n')[line - 1];

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/files/storylets", Body(typed));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        string alert = WebUtility.HtmlDecode(Regex.Match(body, "<div id=\"form-alert\".*?</ul>", RegexOptions.Singleline).Value);
        Assert.Matches("STORYLET_02: cast\\.subject, at character \\d+: No trait named nosuch", alert);
        Assert.Contains($"Line {line}, column {column}", alert);
        Assert.Contains($"{text}\n{new string(' ', column - 1)}^", alert);
        Assert.Equal(before, Everything(app));
    }

    // Slice 1e-calendar: the calendar and its tuning are checked on save like the rest of the pack.
    [Theory]
    [InlineData("calendars", "calendars/base.yaml", "start: { day: 13,", "start: { day: 31,", "calendar_a: start.day must be from 1 to 30, the days in month_b, not 31.")]
    [InlineData("calendar-tuning", "tuning/calendar.yaml", "minDays: 5", "minDays: 9", "cycle.minDays must not be above cycle.maxDays.")]
    [InlineData("chronicle-tuning", "tuning/chronicle.yaml", "death: 5", "death: 6", "weights.death must be a whole number from 0 to 5, not '6'.")]
    public async Task A_calendar_that_cannot_work_is_refused_on_save(string name, string path, string from, string to, string message)
    {
        using TestApp app = NewApp();
        string before = Everything(app);
        string typed = Disk(app, path).Replace(from, to, StringComparison.Ordinal);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), $"/dev/content/files/{name}", Body(typed));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains(message, WebUtility.HtmlDecode(body));
        Assert.Equal(before, Everything(app));
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

        (HttpStatusCode status, _) = await Post(client, "/dev/content/files/pack", Body("id: CORE\nversion: 7\ncalendar: calendar_a\n"));
        (_, string index) = await Get(client, "/dev/content");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("id: CORE\nversion: 7\ncalendar: calendar_a\n", Disk(app, "pack.yaml"));
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

        foreach ((string name, string path) in new[] { ("pack", "pack.yaml"), ("missions", "tuning/missions.yaml"), ("crew-tuning", "tuning/crew.yaml"), ("calendar-tuning", "tuning/calendar.yaml"), ("chronicle-tuning", "tuning/chronicle.yaml"), ("calendars", "calendars/base.yaml") })
        {
            Assert.Contains($"<a href=\"/dev/content/files/{name}\">{path}</a>", body);
        }
    }

    // A storylet that reads trait_c, which nothing raises, and trait_a, which ENCOUNTER_01 raises; trait_b, which ENCOUNTER_04 raises, goes
    // unread. (Since 1e part four an expression reads only a defined trait, so the fixture's trait_c stands in for an unset flag.)
    private const string Unmatched = "- id: STORYLET_09\n  stakes: 1\n  requires: \"ship.trait.trait_c >= 1 && ship.trait.trait_a <= 5\"\n  options:\n"
        + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_c, set: 0 }] }\n"
        + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_c, set: 0 }] }\n";

    // 1e part three: a definition's launch rule sets the trait it belongs to, so a storylet that requires it is no warning; a
    // definition nothing reads or changes is one.
    [Fact]
    public async Task The_traits_page_counts_launch_rules_as_setters_and_warns_of_a_trait_nothing_uses()
    {
        using TestApp app = NewApp();
        string traits = Disk(app, "traits/base.yaml") + "- id: trait_q\n  on: crew\n  launch: { sent: [{ set: 1 }] }\n- id: trait_r\n";
        // The traits first: an expression reads only a defined trait (1e part four).
        await Post(app.NewClient(), "/dev/content/files/traits", Body(traits));
        (HttpStatusCode saved, _) = await Post(app.NewClient(), "/dev/content/files/storylets", Body(Disk(app, "storylets/base.yaml")
            + "- id: STORYLET_09\n  stakes: 1\n  cast:\n    subject: \"subject.trait.trait_q >= 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_q, set: 0 }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_q, set: 0 }] }\n"));
        Assert.Equal(HttpStatusCode.OK, saved);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/traits", Body(traits));

        Assert.Equal(HttpStatusCode.OK, status);
        string shown = WebUtility.HtmlDecode(body);
        Assert.DoesNotContain("Nothing sets trait_q", shown);
        Assert.Contains("trait_r is defined and never used.", shown);
        Assert.DoesNotContain("trait_q is defined and never used.", shown);
        Assert.DoesNotContain("trait_e is defined and never used.", shown);
    }

    [Fact]
    public async Task The_storylets_page_points_to_the_form_and_says_the_file_is_checked_on_save()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content/files/storylets");

        string text = WebUtility.HtmlDecode(body);
        Assert.Contains("Each storylet also opens in its form, from the Storylets list.", text);
        Assert.Contains("Saving checks this file with the rest of the pack.", text);
    }

    // 1e part two: the vignettes file is a text file too, checked by the loader on save.
    [Fact]
    public async Task A_vignettes_save_the_loader_refuses_shows_its_reason_and_writes_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);
        string typed = Disk(app, "vignettes/base.yaml").Replace("  default: option_b\n", "", StringComparison.Ordinal);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/vignettes", Body(typed));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("VIGNETTE_02: A vignette with options needs a default, and it must be one of them.", WebUtility.HtmlDecode(body));
        Assert.Contains("Each vignette also opens in its form, from the Vignettes list.", WebUtility.HtmlDecode(body));
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_storylets_save_the_loader_refuses_shows_its_reason_and_writes_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);
        string typed = Disk(app, "storylets/base.yaml").Replace("  stakes: 2\n", "  stakes: 9\n", StringComparison.Ordinal);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/storylets", Body(typed));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("STORYLET_01: A storylet's stakes must be 1 to 5.", WebUtility.HtmlDecode(body));
        Assert.Contains("Nothing was written.", body);
        Assert.Equal(typed, Typed(body));
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task The_storylets_page_warns_of_a_trait_nothing_sets_and_one_nothing_reads_and_still_saves()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/storylets", Body(Unmatched));
        (_, string page) = await Get(app.NewClient(), "/dev/content/files/storylets");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Unmatched, Disk(app, "storylets/base.yaml"));
        foreach (string shown in new[] { WebUtility.HtmlDecode(body), WebUtility.HtmlDecode(page) })
        {
            Assert.Contains("Nothing sets trait_c on the ship, which STORYLET_09 requires.", shown);
            Assert.Contains("Nothing reads trait_b, which ENCOUNTER_04 sets.", shown);
            Assert.DoesNotContain("Nothing reads trait_a", shown);
            // The warnings sit under the text.
            Assert.True(shown.IndexOf("</textarea>", StringComparison.Ordinal) < shown.IndexOf("Nothing sets trait_c on the ship", StringComparison.Ordinal));
        }
    }

    // An expression that holds with its trait at 0 holds while nothing sets it, so it is no warning.
    [Fact]
    public async Task A_requirement_that_holds_at_zero_is_not_warned_of()
    {
        using TestApp app = NewApp();
        string typed = "- id: STORYLET_09\n  stakes: 1\n  requires: \"ship.trait.trait_a >= 1 && ship.trait.trait_c <= 2 && ship.trait.trait_b >= 0\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_a, set: 0 }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_a, set: 0 }] }\n";

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/storylets", Body(typed));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("Nothing sets", body);
    }

    // trait_a is raised only on the ship (ENCOUNTER_01) and trait_b only on crew (ENCOUNTER_04), so each scope's requirement can miss the other's.
    [Fact]
    public async Task A_trait_required_on_a_scope_nothing_raises_it_on_is_warned_of_for_each_scope()
    {
        using TestApp app = NewApp();
        string typed = "- id: STORYLET_09\n  stakes: 1\n  requires: \"ship.trait.trait_b >= 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_b, set: 0 }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_b, set: 0 }] }\n"
            + "- id: STORYLET_10\n  stakes: 1\n  cast:\n    subject: \"subject.trait.trait_a >= 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_a, set: 0 }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_a, set: 0 }] }\n";

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/storylets", Body(typed));

        Assert.Equal(HttpStatusCode.OK, status);
        string shown = WebUtility.HtmlDecode(body);
        Assert.Contains("Nothing sets trait_b on the ship, which STORYLET_09 requires.", shown);
        Assert.Contains("Nothing sets trait_a on a crew member, which STORYLET_10 requires.", shown);
    }

    // A storylet's own raise counts where it lands: with no on, the ship for a storylet with no cast; on a slot, a crew member.
    [Fact]
    public async Task A_trait_a_storylet_raises_counts_on_its_own_scope_and_subject_counts_as_crew()
    {
        using TestApp app = NewApp();
        string typed = "- id: STORYLET_09\n  stakes: 1\n  cast:\n    subject: \"subject.fit\"\n  requires: \"ship.trait.trait_a == 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_a, set: 0, on: ship }, { trait: trait_c, set: 1, on: ship }, { trait: trait_f, add: 1, on: subject }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_a, set: 0, on: ship }, { trait: trait_c, set: 1, on: ship }] }\n"
            + "- id: STORYLET_10\n  stakes: 1\n  requires: \"ship.trait.trait_c >= 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_c, set: 0 }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_c, set: 0 }] }\n"
            + "- id: STORYLET_11\n  stakes: 1\n  cast:\n    subject: \"subject.trait.trait_f >= 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_f, set: 0 }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_f, set: 0 }] }\n";

        (_, string body) = await Post(app.NewClient(), "/dev/content/files/storylets", Body(typed));

        Assert.DoesNotContain("Nothing sets", body);
    }

    // 1e part four, Task 6: a trait set only in a check's success or failure is set; on the handler, it lands on a crew member.
    [Fact]
    public async Task A_trait_a_check_outcome_raises_counts_as_set()
    {
        using TestApp app = NewApp();
        string typed = "- id: STORYLET_09\n  stakes: 1\n  requires: \"ship.trait.trait_a == 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, check: { skill: SKILL_B, difficulty: 4 }, success: { effects: [{ trait: trait_a, set: 0 }] }, failure: { effects: [{ trait: trait_a, set: 0 }, { trait: trait_c, set: 1 }, { trait: trait_f, add: 1, on: handler }] } }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_a, set: 0 }] }\n"
            + "- id: STORYLET_10\n  stakes: 1\n  requires: \"ship.trait.trait_c >= 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_c, set: 0 }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_c, set: 0 }] }\n"
            + "- id: STORYLET_11\n  stakes: 1\n  cast:\n    subject: \"subject.trait.trait_f >= 1\"\n  options:\n"
            + "    - { id: option_a, label: LABEL_A, effects: [{ trait: trait_f, set: 0 }] }\n"
            + "    - { id: option_b, label: LABEL_B, effects: [{ trait: trait_f, set: 0 }] }\n";

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/storylets", Body(typed));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Saved.", body);
        Assert.DoesNotContain("Nothing sets", body);
    }

    // A save that only warns is a save: it reads "Saved." and lists the warnings under their own heading, not in the list a refusal uses.
    [Fact]
    public async Task A_save_with_only_warnings_says_saved_and_lists_them_apart_from_problems()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/files/storylets", Body(Unmatched));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Saved.", body);
        Assert.DoesNotContain("Nothing was written.", body);
        Assert.Contains("Worth checking", body);
        Assert.Contains("class=\"notes\"", body);
        Assert.DoesNotContain("class=\"problems\"", body);
    }

    [Fact]
    public async Task The_fixtures_storylets_page_has_no_warnings()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content/files/storylets");

        Assert.DoesNotContain("Nothing sets", body);
        Assert.DoesNotContain("Nothing reads", body);
    }

    [Fact]
    public async Task A_missing_storylets_file_opens_empty_and_is_created_on_the_first_save()
    {
        using TestApp app = NewApp(root => File.Delete(Path.Combine(root, "storylets", "base.yaml")));
        HttpClient client = app.NewClient();

        (HttpStatusCode opened, string body) = await Get(client, "/dev/content/files/storylets");
        Assert.Equal(HttpStatusCode.OK, opened);
        Assert.Equal("", Typed(body));

        (HttpStatusCode saved, _) = await Post(client, "/dev/content/files/storylets", Body(Unmatched));

        Assert.Equal(HttpStatusCode.OK, saved);
        Assert.Equal(Unmatched, Disk(app, "storylets/base.yaml"));
        Assert.Contains(app.Live.Pack.Storylets, s => s.Id == "STORYLET_09");
    }

    [Fact]
    public async Task The_repositorys_own_content_is_byte_identical_after_text_saves()
    {
        using TestApp app = NewApp();

        await Post(app.NewClient(), "/dev/content/files/pack", Body("id: CORE\nversion: 5\ncalendar: calendar_a\n"));

        guard.Verify();
    }
    // Test ships (1e part six, design 4.6): a save of their file is judged with them, so a ship naming someone the pack lacks is
    // refused in the loader's words, and nothing is written.
    [Fact]
    public async Task A_test_ship_naming_a_crew_member_the_pack_lacks_is_refused()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);
        string typed = Disk(app, "testships/base.yaml").Replace("{ id: CREW_03, fit: false }", "{ id: CREW_99, fit: false }", StringComparison.Ordinal);

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/files/testships", Body(typed));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("test_ship_a: crew[1].id: no crew member CREW_99 in the starting crew.", WebUtility.HtmlDecode(body));
        Assert.Equal(before, Everything(app));
    }

    // Any other save is judged by the pack alone: one that leaves a ship naming a storylet that is gone saves, and the index lists
    // the ship's problem beside a valid pack.
    [Fact]
    public async Task A_save_that_leaves_a_test_ship_stale_saves_and_the_index_says_so()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string typed = Disk(app, "testships/base.yaml").Replace("{ id: STORYLET_01, daysAgo: 40, cyclesAgo: 7 }", "{ id: STORYLET_01, daysAgo: 40, cyclesAgo: 7 }\n    - { id: STORYLET_06, daysAgo: 50, cyclesAgo: 8 }", StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/dev/content/files/testships", Body(typed))).Status);
        string storylets = Disk(app, "storylets/base.yaml");
        string without = storylets[..storylets.IndexOf("- id: STORYLET_06", StringComparison.Ordinal)];

        (HttpStatusCode status, _) = await Post(client, "/dev/content/files/storylets", Body(without));
        (_, string index) = await Get(client, "/dev/content");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<span class=\"res\">Valid</span>", index);
        Assert.Contains("test_ship_a: storylets[1].id: no storylet or vignette STORYLET_06.", WebUtility.HtmlDecode(index));
    }
}

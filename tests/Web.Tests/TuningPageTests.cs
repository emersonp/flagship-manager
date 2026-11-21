using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sim.Content;
using Web.Tuning;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// The Tuning page and its draft form (1f part one, Task 9), through the real app on a scratch copy of the content that carries the game's
// own tuning/missions.yaml.
public sealed class TuningPageTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private const string File = TuningDrafts.Missions;

    private static string GameText => System.IO.File.ReadAllText(Path.Combine(TestApp.RepositoryRoot(), "content", "tuning", "missions.yaml"));

    // A scratch app whose missions.yaml is the game's.
    private static TestApp GameTuningApp()
    {
        return NewApp(root => System.IO.File.WriteAllText(Path.Combine(root, "tuning", "missions.yaml"), GameText));
    }

    private static string Row(string path) => Web.Forms.FormValues.Slug(path) + "-row";

    // The element with this id, up to the next row or the end of the body: enough to read one row's own words.
    private static string RowHtml(string body, string path)
    {
        int start = body.IndexOf($"id=\"{Row(path)}\"", StringComparison.Ordinal);
        start = start < 0 ? start : body.LastIndexOf("<div", start, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no row for {path}");
        int next = body.IndexOf("-row\"", body.IndexOf(Row(path), start, StringComparison.Ordinal) + Row(path).Length + 1, StringComparison.Ordinal);
        return next < 0 ? body[start..] : body[start..next];
    }

    private static string Encoded(string text) => HtmlEncoder.Default.Encode(text);

    [Fact]
    public async Task The_page_lists_every_field_of_the_games_missions_yaml_in_file_order_with_its_hint_and_the_read_only_sections_as_read_only()
    {
        using TestApp app = GameTuningApp();
        Assert.Equal(GameText, Disk(app, File));

        (HttpStatusCode status, string body) = await Get(app.NewClient(), "/dev/tuning");

        Assert.Equal(HttpStatusCode.OK, status);
        List<TuningField> fields = TuningFields.Read(GameText).SelectMany(s => s.Fields).ToList();
        int at = -1;
        foreach ((TuningField field, int i) in fields.Select((f, i) => (f, i)))
        {
            int row = body.IndexOf($"id=\"{Row(field.Path)}\"", StringComparison.Ordinal);
            Assert.True(row > at, $"{field.Path} is not in file order");

            // The hint is in the field's own row, or at the head of the group it opens, just above the row.
            if (field.Hint is not null)
            {
                int hint = body.IndexOf(Encoded(field.Hint), at < 0 ? 0 : at, StringComparison.Ordinal);
                int end = i + 1 < fields.Count ? body.IndexOf($"id=\"{Row(fields[i + 1].Path)}\"", StringComparison.Ordinal) : body.Length;
                Assert.True(hint > at && hint < end, $"{field.Path}'s hint is not beside it");
            }

            string html = RowHtml(body, field.Path);
            if (DraftView.ReadOnlySections.Contains(field.Section))
            {
                Assert.Contains("tf--ro", html);
                Assert.DoesNotContain("<input", html);
                Assert.Contains($"<span class=\"tf__val m\">{DraftView.Shown(field.Kind, field.Values)}</span>", html);
            }
            else
            {
                foreach ((string name, string value) in DraftView.Names(field).Zip(field.Values))
                {
                    Assert.Equal(value, Value(body, name));
                }
            }

            at = row;
        }

        // Every read-only section is folded under the one label, which links to the text view; no input names one of their paths.
        Assert.Contains("Measured in Legacy mode (part two); <a href=\"/dev/content/files/missions\">edit as text</a> for now.", body);
        foreach (string section in DraftView.ReadOnlySections)
        {
            Assert.Matches($"<details class=\"tf-ro__sec\"><summary>{section} <span", body);
            Assert.DoesNotContain($"name=\"{section}.", body);
        }

        // The tiers' comment heads their group, as Task 3 gave it to their first field.
        Assert.Contains("<span class=\"tf-group__key\" id=\"f-check-tiers-g\">tiers</span> <span class=\"hint\">the lowest margin for each tier; below failure is a disaster</span>", body);
        Assert.Contains("No draft.", body);
        Assert.DoesNotContain("id=\"draft-save\"", body);
    }

    [Fact]
    public async Task Posting_a_field_shows_the_saved_value_beside_it_and_a_bad_value_shows_the_loaders_error_beside_it()
    {
        using TestApp app = GameTuningApp();
        HttpClient client = app.NewClient();
        string before = Disk(app, File);

        (HttpStatusCode status, string body) = await Post(client, "/dev/tuning/draft?path=check.target", "check.target=9", datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("event: datastar-patch-elements", body);
        Assert.Contains("id=\"draft-head\"", body);
        Assert.Contains("<span class=\"m\">1</span> change. Begun at", body);
        string row = RowHtml(body, "check.target");
        Assert.StartsWith("<div class=\"tf tf--changed\"", row);
        Assert.Contains("saved <span class=\"m\">7</span>", row);
        Assert.Equal("9", Value(body, "check.target"));

        // A dice field: both values posted, the saved pair beside it.
        (_, body) = await Post(client, "/dev/tuning/draft?path=injury.dice", "injury.dice.count=3&injury.dice.sides=6", datastar: true);
        Assert.Contains("saved <span class=\"m\">2 dice of 6 sides</span>", RowHtml(body, "injury.dice"));
        Assert.Contains("<span class=\"m\">2</span> changes.", body);

        // Over the cap: the loader's own words beside the field, and the box marked.
        (_, body) = await Post(client, "/dev/tuning/draft?path=compounding.collapseAt", "compounding.collapseAt=6", datastar: true);
        row = RowHtml(body, "compounding.collapseAt");
        Assert.Contains("<p class=\"error\" id=\"f-compounding-collapseAt-error\">compounding.collapseAt must not be above compounding.cap.</p>", row);
        Assert.Contains("aria-invalid=\"true\"", row);

        // Raising the cap clears that error, so the row it was on is patched too.
        (_, body) = await Post(client, "/dev/tuning/draft?path=compounding.cap", "compounding.cap=6", datastar: true);
        Assert.DoesNotContain("must not be above", RowHtml(body, "compounding.collapseAt"));

        // The saved value again takes the field out of the draft. Nothing was written.
        (_, body) = await Post(client, "/dev/tuning/draft?path=check.target", "check.target=7", datastar: true);
        Assert.DoesNotContain("saved <span", RowHtml(body, "check.target"));
        Assert.Equal(before, Disk(app, File));

        // A read-only section's number is not the form's to change.
        (status, _) = await Post(client, "/dev/tuning/draft?path=salience.stakes", "salience.stakes=11", datastar: true);
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Two_requests_see_one_draft_and_discard_ends_it()
    {
        using TestApp app = GameTuningApp();

        await Post(app.NewClient(), "/dev/tuning/draft?path=check.gearBonus", "check.gearBonus=3", datastar: true);
        (_, string body) = await Get(app.NewClient(), "/dev/tuning");

        Assert.Equal("3", Value(body, "check.gearBonus"));
        Assert.Contains("saved <span class=\"m\">2</span>", RowHtml(body, "check.gearBonus"));
        Assert.Contains("id=\"draft-save\"", body);

        (_, body) = await Post(app.NewClient(), "/dev/tuning/discard", "", datastar: true);
        Assert.Contains("No draft.", body);
        Assert.Equal("2", Value(body, "check.gearBonus"));
        Assert.Null(app.Services.GetRequiredService<TuningDrafts>().Current(File));
    }

    [Fact]
    public async Task Save_writes_the_file_in_place_and_says_what_the_next_mission_uses()
    {
        using TestApp app = GameTuningApp();
        HttpClient client = app.NewClient();
        string before = Disk(app, File);

        await Post(client, "/dev/tuning/draft?path=check.target", "check.target=9", datastar: true);
        await Post(client, "/dev/tuning/draft?path=bench.teams.rookie.skill", "bench.teams.rookie.skill.0=2&bench.teams.rookie.skill.1=3", datastar: true);
        (HttpStatusCode status, string body) = await Post(client, "/dev/tuning/save", "", datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        string expected = before.Replace("  target: 7 ", "  target: 9 ", StringComparison.Ordinal).Replace("rookie:  { size: 3, skill: [1, 3]", "rookie:  { size: 3, skill: [2, 3]", StringComparison.Ordinal);
        Assert.NotEqual(before, expected);
        Assert.Equal(expected, Disk(app, File));
        Assert.Equal(9, app.Live.Pack.Tuning.CheckTarget);
        Assert.Contains("Saved tuning/missions.yaml: <span class=\"m\">check.target</span> is now <span class=\"m\">9</span> and <span class=\"m\">bench.teams.rookie.skill</span> is now <span class=\"m\">2 to 3</span>. A mission in flight keeps the tuning it started with; the next one uses this.", body);
        Assert.Contains("No draft.", body);
        Assert.Equal("9", Value(body, "check.target"));
    }

    [Fact]
    public async Task A_stale_drafts_save_is_refused_naming_the_keys()
    {
        using TestApp app = GameTuningApp();
        HttpClient client = app.NewClient();
        await Post(client, "/dev/tuning/draft?path=check.target", "check.target=9", datastar: true);
        string edited = Disk(app, File).Replace("  gearBonus: 2\n", "  gearBonus: 3\n", StringComparison.Ordinal);
        System.IO.File.WriteAllText(Path.Combine(app.ContentPath, File), edited);

        // Said at the draft's head as soon as the page is drawn.
        (_, string page) = await Get(client, "/dev/tuning");
        Assert.Contains("The file has changed since this draft began", page);
        Assert.Contains("Saved values changed under the draft: <span class=\"m\">check.gearBonus</span>. Saving will be refused.", page);

        (HttpStatusCode status, string body) = await Post(client, "/dev/tuning/save", "", datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("The save was refused. Nothing was written.", body);
        Assert.Contains("tuning/missions.yaml has changed since this draft began, so it is not saved.", body);
        Assert.Contains("check.gearBonus has changed in the file since this draft began.", RowHtml(body, "check.gearBonus"));
        Assert.Equal(edited, Disk(app, File));

        // Without Datastar, the whole page with the refusal's status.
        (status, _) = await Post(client, "/dev/tuning/save", "");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
    }

    [Fact]
    public async Task A_save_the_loader_refuses_shows_its_words_beside_the_field()
    {
        using TestApp app = GameTuningApp();
        HttpClient client = app.NewClient();
        string before = Disk(app, File);

        await Post(client, "/dev/tuning/draft?path=compounding.collapseAt", "compounding.collapseAt=6", datastar: true);
        (_, string body) = await Post(client, "/dev/tuning/save", "", datastar: true);

        Assert.Contains("The save was refused. Nothing was written. The reasons are beside their fields.", body);
        Assert.Contains("compounding.collapseAt must not be above compounding.cap.", RowHtml(body, "compounding.collapseAt"));
        Assert.Equal(before, Disk(app, File));
    }

    [Fact]
    public async Task The_routes_are_absent_outside_developer_mode()
    {
        using var app = new TestApp();
        HttpClient client = app.NewClient();

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/tuning")).Status);
        foreach (string url in new[] { "/dev/tuning/draft?path=check.target", "/dev/tuning/discard", "/dev/tuning/save" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(url, new FormUrlEncodedContent([new("check.target", "9")]))).StatusCode);
        }
    }

    [Fact]
    public void The_query_carries_every_input_and_a_seed_as_text()
    {
        var query = new TuningQuery
        {
            View = "every",
            Objective = "OBJECTIVE_TEMPLATE_1",
            Draw = "fixed",
            Runs = "500",
            Seed = "18446744073709551615",
            Team = "legacy",
            Strength = "sweep",
            Source = "1a2b",
            Crew = ["CREW_01", "CREW_03"],
            Lead = "CREW_03",
            Style = "reckless",
            Priority = "1",
            Autonomy = "-1",
            Develop = true,
            Gear = ["scanner", "medical"],
            Policy = "withdraw",
        };

        string href = query.Href();
        TuningQuery back = TuningQuery.From(QueryHelpers(href));

        Assert.StartsWith("/dev/tuning?view=every&", href);
        Assert.Contains("seed=18446744073709551615", href);
        Assert.Equal(query.Href(), back.Href());
        Assert.Equal(query.Crew, back.Crew);
        Assert.Equal(query.Gear, back.Gear);
        Assert.Equal("/dev/tuning", TuningQuery.Default.Href());
        Assert.Equal("1000", (TuningQuery.Default with { View = "every" }).RunsOrDefault());

        // A word the page does not know is its default, and an unknown seed stays as typed for the inputs to judge.
        TuningQuery odd = TuningQuery.From(QueryHelpers("/dev/tuning?policy=sneaky&strength=elite&seed=-3"));
        Assert.Equal("back-lead", odd.Policy);
        Assert.Equal("mixed", odd.Strength);
        Assert.Equal("-3", odd.Seed);
    }

    [Fact]
    public async Task A_reload_and_a_link_land_on_the_same_inputs()
    {
        using TestApp app = GameTuningApp();
        var query = new TuningQuery { Objective = "OBJECTIVE_TEMPLATE_1", Seed = "42", Strength = "sweep" };

        (_, string body) = await Get(app.NewClient(), query.Href());

        // The view tabs keep every other input.
        Assert.Contains($"href=\"{Encoded((query with { View = "every" }).Href())}\"", body);
        Assert.Contains($"href=\"{Encoded(query.Href())}\" aria-current=\"page\">One mission", body);
    }

    // Nothing here writes to the repository's content: every app runs on a scratch copy.
    [Fact]
    public void The_repository_content_is_untouched()
    {
        guard.Verify();
    }

    private static IQueryCollection QueryHelpers(string href)
    {
        string query = href.Contains('?', StringComparison.Ordinal) ? href[href.IndexOf('?', StringComparison.Ordinal)..] : "";
        return new QueryCollection(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query));
    }
}

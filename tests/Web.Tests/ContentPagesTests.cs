using System.Net;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core.Content;
using Sim.Crew;
using Sim.Missions;

namespace Web.Tests;

// The editor's pages and the swappable pack, through the real app on a scratch copy of the content.
public sealed class ContentPagesTests : IClassFixture<RepoContentGuard>
{
    private static readonly string[] Segments = ["skills", "gear", "crew", "assignments", "objectives", "encounters"];

    private static ContentFileSet Repo()
    {
        return ContentFileSet.ReadDirectory(Path.Combine(TestApp.RepositoryRoot(), "content"));
    }

    private static string Segment(ContentKind kind)
    {
        return Segments[(int)kind];
    }

    private static async Task<(HttpStatusCode Status, string Body)> Get(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task With_developer_mode_off_every_editor_route_is_404_and_no_content_link_shows()
    {
        using var app = new TestApp();
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/content")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/content/crew")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/dev/content/reload", new FormUrlEncodedContent([]))).StatusCode);
        Assert.DoesNotContain("/dev/content", (await Get(client, "/")).Body);
        Assert.DoesNotContain("/dev/content", (await Get(client, $"/legacy/{id}")).Body);
    }

    [Fact]
    public async Task With_developer_mode_on_the_index_lists_the_pack_every_kind_and_every_item()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        ContentFileSet files = Repo();

        (HttpStatusCode status, string body) = await Get(client, "/dev/content");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(app.Live.Pack.Id, body);
        Assert.Contains("Valid", body);
        Assert.Contains("Names and gear reach games in progress at once. A legacy's crew keep the levels they started with.", WebUtility.HtmlDecode(body));
        Assert.Contains("href=\"/\"", body);
        foreach (ContentKind kind in Enum.GetValues<ContentKind>())
        {
            Assert.Contains($"href=\"/dev/content/{Segment(kind)}\"", body);
        }

        foreach (ContentItemRef item in files.Index())
        {
            Assert.Contains($">{item.Id}</", body);
            Assert.Contains(item.File, body);
        }

        Assert.Contains("tuning/missions.yaml", body);
        Assert.Contains("tuning/crew.yaml", body);
        Assert.Contains("pack.yaml", body);
        Assert.Equal(Enum.GetValues<ContentKind>().Length, Regex.Matches(body, "class=\"well kind\"").Count);
    }

    [Fact]
    public async Task The_index_counts_legacies_and_the_content_link_shows_on_the_legacies_page_and_the_top_bar()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_TWO", 2);

        string index = (await Get(client, "/dev/content")).Body;

        Assert.Matches(@"<span class=""m"">2</span> legacies", index);
        Assert.Contains("<a class=\"home\" href=\"/dev/content\">Content</a>", (await Get(client, "/")).Body);
        Assert.Contains("<a class=\"home\" href=\"/dev/content\">Content</a>", (await Get(client, $"/legacy/{id}")).Body);
    }

    [Fact]
    public async Task Each_kinds_page_lists_its_items_with_links_to_their_forms_and_a_new_link()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        ContentFileSet files = Repo();

        foreach (ContentKind kind in Enum.GetValues<ContentKind>())
        {
            (HttpStatusCode status, string body) = await Get(client, $"/dev/content/{Segment(kind)}");

            Assert.Equal(HttpStatusCode.OK, status);
            foreach (ContentItemRef item in files.Index().Where(i => i.Kind == kind))
            {
                Assert.Contains($"href=\"/dev/content/{Segment(kind)}/{item.Id}\"", body);
            }

            Assert.Contains($"href=\"/dev/content/{Segment(kind)}/new\"", body);
            Assert.Contains("href=\"/dev/content\"", body);
        }

        Assert.Contains(">Seniority</th>", (await Get(client, "/dev/content/crew")).Body);
        Assert.Contains(">Tag</th>", (await Get(client, "/dev/content/gear")).Body);
    }

    [Theory]
    [InlineData("/dev/content/nothing")]
    [InlineData("/dev/content/Crew")]
    [InlineData("/dev/content/crews")]
    public async Task An_unknown_kind_is_404(string url)
    {
        using var app = new TestApp(developerMode: true, scratch: true);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(app.NewClient(), url)).Status);
    }

    [Fact]
    public async Task Names_and_ids_are_html_encoded_on_the_index_and_the_lists()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        await app.Editor.Save(files => ContentEdits.Upsert(files, ContentKind.Crew, ContentEdits.Get<CrewModel>(files, ContentKind.Crew, "CREW_01")! with { Name = TestApp.OddName }));

        string index = (await Get(client, "/dev/content")).Body;
        string list = (await Get(client, "/dev/content/crew")).Body;

        foreach (string body in new[] { index, list })
        {
            Assert.Contains("NAME 7 &lt;b&gt; &amp; -3", body);
            Assert.DoesNotContain("<b>", body);
        }
    }

    [Fact]
    public async Task An_item_the_editor_cannot_keep_is_marked_and_a_file_problem_is_listed_read_only()
    {
        using var app = new TestApp(developerMode: true, scratch: true, prepare: root =>
        {
            string gear = Path.Combine(root, "gear", "base.yaml");
            File.WriteAllText(gear, File.ReadAllText(gear).Replace("tag: scanner", "tag: scanner, typo: 1", StringComparison.Ordinal));
            string skills = Path.Combine(root, "skills", "base.yaml");
            File.AppendAllText(skills, "\n---\n- id: SKILL_Z\n");
        });
        HttpClient client = app.NewClient();

        string body = (await Get(client, "/dev/content")).Body;
        string list = (await Get(client, "/dev/content/gear")).Body;

        Assert.Contains("Has content the editor cannot keep", body);
        Assert.Contains("Has content the editor cannot keep", list);
        Assert.Contains("second document", body);
        Assert.Contains("skills/base.yaml", body);
    }

    [Fact]
    public async Task A_missing_content_folder_is_named_in_plain_words()
    {
        using var app = new TestApp(developerMode: true, scratch: true, prepare: root => Directory.Delete(root, true));
        HttpClient client = app.NewClient();

        (HttpStatusCode status, string body) = await Get(client, "/dev/content");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("The content folder was not found: ", body);
        Assert.Contains(app.ContentPath, body);
    }

    // A request that takes its snapshot before a swap renders with the old pack, and the next one with the new.
    [Fact]
    public async Task A_page_loaded_before_a_swap_renders_with_the_old_pack_and_the_next_with_the_new_one()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        app.Sessions.AfterLoad = _ =>
        {
            app.Sessions.AfterLoad = null;
            app.Host.Swap(NamedPack.Of(app.Live.Pack));
        };

        string during = (await Get(client, $"/legacy/{id}")).Body;
        string after = (await Get(client, $"/legacy/{id}")).Body;

        Assert.Equal(2, app.Live.Generation);
        Assert.DoesNotContain(NamedPack.Prefix, during);
        Assert.Contains(NamedPack.Prefix + "CREW_01", after);
    }

    // The commands, the commit, and the stream all use the pack the request started on.
    [Fact]
    public async Task An_advance_that_is_swapped_over_mid_request_answers_with_the_old_names()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        app.Sessions.AfterCommit = _ =>
        {
            app.Sessions.AfterCommit = null;
            app.Host.Swap(NamedPack.Of(app.Live.Pack));
        };

        var captain = new WebCaptain(app, client, id);
        string stream = await captain.Post("advance", []);
        string page = (await Get(client, $"/legacy/{id}")).Body;

        Assert.DoesNotContain(NamedPack.Prefix, stream);
        Assert.Contains(NamedPack.Prefix + "CREW_01", page);
    }

    [Fact]
    public async Task A_legacy_waiting_on_a_call_still_answers_after_a_swap_that_renames_a_crew_member_and_shows_the_new_name()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        (Guid id, WebCaptain captain) = await WaitingCall(app, client);
        await app.Editor.Save(files => ContentEdits.Upsert(files, ContentKind.Crew, ContentEdits.Get<CrewModel>(files, ContentKind.Crew, "CREW_01")! with { Name = "NAME_PLACEHOLDER_RENAMED" }));

        int eventsBefore = app.Game.Store.ReadEvents(id).Count;
        string stream = await captain.Post("answer", AnswerForm(await captain.Peek()));
        string page = (await Get(client, $"/legacy/{id}")).Body;

        Assert.Contains("event: datastar-patch-elements", stream);
        Assert.Contains("NAME_PLACEHOLDER_RENAMED", page);
        Assert.True(app.Game.Store.ReadEvents(id).Count > eventsBefore, "The answer was accepted: the stored legacy moved on.");
    }

    [Fact]
    public async Task A_new_legacy_after_a_swap_starts_with_the_changed_skill_level()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        CrewModel before = ContentEdits.Get<CrewModel>(app.Editor.Read(), ContentKind.Crew, "CREW_01")!;
        int level = before.Skills["SKILL_A"] == 1 ? 2 : 1;
        await app.Editor.Save(files => ContentEdits.Upsert(files, ContentKind.Crew, before with { Skills = new Dictionary<string, int>(before.Skills) { ["SKILL_A"] = level } }));

        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;

        CrewMember member = legacy.State.Module<CrewState>(CrewModule.Name).Members.Single(m => m.Id == "CREW_01");
        Assert.Equal(level, member.Skills["SKILL_A"].Level);
    }

    [Fact]
    public async Task Starting_in_developer_mode_with_an_invalid_pack_answers_every_game_route_with_one_page_and_keeps_the_editor()
    {
        using var app = new TestApp(developerMode: true, scratch: true, prepare: root => File.WriteAllText(Path.Combine(root, "pack.yaml"), "not: [valid"));
        HttpClient client = app.NewClient();
        Guid id = Guid.NewGuid();

        foreach (string url in new[] { "/", $"/legacy/{id}" })
        {
            (HttpStatusCode status, string body) = await Get(client, url);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Contains("The content pack is invalid", body);
            Assert.Contains("pack.yaml", body);
            Assert.Contains("href=\"/dev/content\"", body);
            Assert.Contains("<link rel=\"stylesheet\" href=\"/css/app.css\">", body);
        }

        HttpResponseMessage post = await client.PostAsync("/legacies", new FormUrlEncodedContent([new KeyValuePair<string, string>("name", "LEGACY")]));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, post.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, "/dev/content")).Status);
        Assert.Contains("class=\"problems\"", (await Get(client, "/dev/content")).Body);
    }

    [Fact]
    public async Task A_datastar_post_with_no_valid_pack_gets_the_same_words_as_a_patch_of_the_main_screen()
    {
        using var app = new TestApp(developerMode: true, scratch: true, prepare: root => File.WriteAllText(Path.Combine(root, "pack.yaml"), "not: [valid"));
        HttpClient client = app.NewClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/legacy/{Guid.NewGuid()}/advance") { Content = new FormUrlEncodedContent([]) };
        request.Headers.Add("Datastar-Request", "true");

        HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event: datastar-patch-elements", body);
        Assert.Contains("<main id=\"main\"", body);
        Assert.Contains("The content pack is invalid", body);
    }

    [Fact]
    public async Task Reload_from_disk_swaps_in_a_pack_fixed_by_hand_and_the_game_routes_come_back()
    {
        string pack = "";
        using var app = new TestApp(developerMode: true, scratch: true, prepare: root =>
        {
            pack = File.ReadAllText(Path.Combine(root, "pack.yaml"));
            File.WriteAllText(Path.Combine(root, "pack.yaml"), "not: [valid");
        });
        HttpClient client = app.NewClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Get(client, "/")).Status);

        HttpResponseMessage stillBroken = await client.PostAsync("/dev/content/reload", new FormUrlEncodedContent([]));
        string stillBrokenBody = await stillBroken.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Get(client, "/")).Status);
        Assert.Contains("still invalid", stillBrokenBody);

        File.WriteAllText(Path.Combine(app.ContentPath, "pack.yaml"), pack);
        HttpResponseMessage fixedResponse = await client.PostAsync("/dev/content/reload", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.OK, fixedResponse.StatusCode);
        Assert.Contains("Reloaded", await fixedResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await Get(client, "/")).Status);
        Assert.NotNull(app.Host.Current);
    }

    [Fact]
    public async Task A_reload_that_finds_the_disk_invalid_while_a_pack_is_live_says_the_game_keeps_the_pack_it_had()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        _ = app.Host;
        File.WriteAllText(Path.Combine(app.ContentPath, "pack.yaml"), "not: [valid");

        HttpResponseMessage response = await app.NewClient().PostAsync("/dev/content/reload", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("The pack on disk is invalid, so the game keeps the pack it had.", await response.Content.ReadAsStringAsync());
        Assert.NotNull(app.Host.Current);
    }

    // The launch is a paced route: its commands, stored events, and stream all come from the pack the request started on.
    [Fact]
    public async Task A_launch_that_is_swapped_over_mid_request_uses_the_pack_it_started_with_in_the_stream_and_in_the_store()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid control = await app.Sessions.CreateLegacy(app.Live, "CONTROL", 7);
        Guid swapped = await app.Sessions.CreateLegacy(app.Live, "SWAPPED", 7);
        var controlCaptain = new WebCaptain(app, client, control);
        var swappedCaptain = new WebCaptain(app, client, swapped);
        await ToLaunch(controlCaptain);
        await ToLaunch(swappedCaptain);
        (string route, _) = await controlCaptain.Step();
        Assert.Equal("launch", route);
        ContentPack changed = ContentLoader.Load(app.Editor.Read().Files.ToDictionary(f => f.Key, f => f.Key == "tuning/missions.yaml" ? f.Value.Replace("dice: { count: 2, sides: 6 }   # every check", "dice: { count: 1, sides: 20 }   # every check", StringComparison.Ordinal) : f.Value)).Pack!;
        // The first load is the captain's own read; the second is the launch request, after it took its snapshot.
        int loads = 0;
        app.Sessions.AfterLoad = _ =>
        {
            if (++loads == 2)
            {
                app.Sessions.AfterLoad = null;
                app.Host.Swap(NamedPack.Of(changed));
            }
        };

        (route, string stream) = await swappedCaptain.Step();

        Assert.Equal("launch", route);
        Assert.Equal(2, app.Live.Generation);
        Assert.DoesNotContain(NamedPack.Prefix, stream);
        Assert.Equal(Stored(app, control), Stored(app, swapped));
    }

    // Datastar writes a true value as an empty attribute, so the in-flight style needs the expression that gives "true".
    [Fact]
    public async Task The_games_buttons_write_aria_busy_as_true_while_in_flight_and_remove_it_otherwise()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        (Guid waitingId, _) = await WaitingCall(app, client);
        string call = System.Net.WebUtility.HtmlDecode((await Get(client, $"/legacy/{waitingId}")).Body);
        Guid other = await app.Sessions.CreateLegacy(app.Live, "OTHER", 3);
        string orders = System.Net.WebUtility.HtmlDecode((await Get(client, $"/legacy/{other}")).Body);
        await ToLaunch(new WebCaptain(app, client, other));
        string briefing = System.Net.WebUtility.HtmlDecode((await Get(client, $"/legacy/{other}")).Body);

        foreach (string page in new[] { call, orders, briefing })
        {
            MatchCollection busy = Regex.Matches(page, "data-attr:aria-busy=\"([^\"]*)\"");
            Assert.NotEmpty(busy);
            Assert.All(busy.Select(m => m.Groups[1].Value), v => Assert.EndsWith("? 'true' : false", v));
        }
    }

    private static async Task ToLaunch(WebCaptain captain)
    {
        while (await captain.Peek() is not AssignTeam)
        {
            await captain.Step();
        }
    }

    // What the commits stored, without the ids that differ between two legacies.
    private static List<string> Stored(TestApp app, Guid id)
    {
        return app.Game.Store.ReadEvents(id).Select(e => $"{e.Cycle} {e.Type} {e.Payload.Replace(id.ToString(), "LEGACY", StringComparison.OrdinalIgnoreCase)}").ToList();
    }

    [Fact]
    public async Task A_datastar_reload_patches_the_main_screen()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        var request = new HttpRequestMessage(HttpMethod.Post, "/dev/content/reload") { Content = new FormUrlEncodedContent([]) };
        request.Headers.Add("Datastar-Request", "true");

        HttpResponseMessage response = await app.NewClient().SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<main id=\"main\"", body);
        Assert.Contains("Reloaded", body);
    }

    [Fact]
    public void Starting_with_an_invalid_pack_and_developer_mode_off_fails_with_the_loaders_errors()
    {
        using var app = new TestApp(scratch: true, prepare: root => File.WriteAllText(Path.Combine(root, "pack.yaml"), "not: [valid"));

        Exception e = Assert.ThrowsAny<Exception>(() => app.NewClient());

        Assert.Contains("The content pack is invalid", e.ToString());
    }

    private static List<KeyValuePair<string, string>> AnswerForm(Sim.Core.ICommand command)
    {
        var answer = (AnswerCall)command;
        return [new("mission", answer.MissionId), new("call", answer.CallId), new("answer", answer.Answer.ToString())];
    }

    private static async Task<(Guid Id, WebCaptain Captain)> WaitingCall(TestApp app, HttpClient client)
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            Guid id = await app.Sessions.CreateLegacy(app.Live, $"LEGACY_{seed}", seed);
            var captain = new WebCaptain(app, client, id);
            for (int steps = 0; steps < 50; steps++)
            {
                (string route, _) = await captain.Step();
                if (route == "launch")
                {
                    LoadedLegacy legacy = await captain.Read();
                    if (legacy.State.TryModule(MissionsModule.Name, out MissionsState? m) && m.Site?.Mission?.State.Pending is not null)
                    {
                        return (id, captain);
                    }

                    break;
                }
            }
        }

        throw new InvalidOperationException("No seed to 200 left a call waiting.");
    }
}

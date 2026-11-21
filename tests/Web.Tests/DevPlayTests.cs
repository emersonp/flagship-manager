using System.Net;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;
using Sim.Storylets;
using Sim.Traits;

namespace Web.Tests;

// The legacies page's Play and Dev Play, and Dev Play's panel of traits, through the real app.
public sealed class DevPlayTests
{
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields)
    {
        return new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
    }

    private static async Task<(HttpStatusCode Status, string Body)> Get(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    private static async Task<string> Post(HttpClient client, string url, FormUrlEncodedContent form)
    {
        HttpResponseMessage response = await client.PostAsync(url, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Each_legacy_has_a_play_button_and_its_name_is_not_a_link_and_there_is_no_dev_play_outside_developer_mode()
    {
        using var app = new TestApp();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);

        (_, string list) = await Get(app.NewClient(), "/");

        Assert.Contains("<h3 class=\"legacy__name\" id=\"legacy-" + id.ToString("N") + "\">LEGACY_ONE</h3>", list);
        Assert.Contains($"<a class=\"btn\" href=\"/legacy/{id}\"", list);
        Assert.DoesNotContain("Dev Play", list);
        Assert.DoesNotContain("<a href=\"/legacy/", list);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(app.NewClient(), $"/dev/legacy/{id}")).Status);
    }

    [Fact]
    public async Task In_developer_mode_dev_play_opens_the_bridge_with_the_panel_and_play_opens_it_without()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);

        (_, string list) = await Get(client, "/");
        (HttpStatusCode status, string dev) = await Get(client, $"/dev/legacy/{id}");
        (_, string play) = await Get(client, $"/legacy/{id}");

        Assert.Contains($"<a class=\"btn btn--dev\" href=\"/dev/legacy/{id}\"", list);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<header id=\"topbar\">", dev);
        Assert.Contains("<section id=\"dev\"", dev);
        Assert.Contains("No traits are set.", dev);
        Assert.Contains("<title>LEGACY_ONE, Dev Play</title>", dev);
        // The form suggests every trait the pack reads or changes.
        Assert.Contains("<option value=\"trait_a\"></option>", dev);
        Assert.DoesNotContain("id=\"dev\"", play);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, $"/dev/legacy/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task Setting_a_trait_stores_it_as_the_developers_and_answers_with_the_panel()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);
        LoadedLegacy before = (await app.Sessions.Read(app.Live, id))!;
        string crewId = app.Live.Game.Bus.View<IRosterView>(before.State).Roster[1];
        string crew = app.Live.Names.Crew(crewId);

        string ship = await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_a"), ("on", "ship"), ("value", "1")));
        string member = await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_b"), ("on", crewId), ("value", "3")));

        Assert.Contains("event: datastar-patch-elements", ship);
        Assert.Contains("trait_a is now 1 on the ship.", ship);
        Assert.Contains($"trait_b is now 3 on {crew}.", member);
        Assert.Contains("<td class=\"dev__trait\">trait_b</td>", member);
        LoadedLegacy after = (await app.Sessions.Read(app.Live, id))!;
        TraitsState traits = after.State.Module<TraitsState>(TraitsModule.Name);
        Assert.Equal([("trait_a", (string?)null, 1), ("trait_b", crewId, 3)], traits.Traits.Select(t => (t.Trait, t.CrewId, t.Value)));
        Assert.Equal(2, app.Game.Store.ReadEvents(id, 1).Count(e => e.Type == "TraitSetByDeveloper"));

        string cleared = await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_a"), ("on", "ship"), ("value", "0")));
        Assert.Contains("Cleared trait_a on the ship.", cleared);
    }

    [Theory]
    [InlineData("Trait A", "1", "Trait id 'Trait A' must be lower case")]
    [InlineData("trait_a", "x", "A trait's value is 0 to 99.")]
    [InlineData("trait_a", "100", "A trait's value is 0 to 99.")]
    [InlineData("trait_a", "0", "trait_a is already 0 on the ship.")]
    public async Task A_refused_trait_says_why_keeps_what_was_typed_and_changes_nothing(string trait, string value, string reason)
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);

        string body = await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", trait), ("on", "ship"), ("value", value)));

        Assert.Contains("<p id=\"dev-result\" class=\"error\" role=\"status\">" + reason, body);
        Assert.Contains($"value=\"{trait}\"", body);
        Assert.Contains($"value=\"{value}\"", body);
        Assert.DoesNotContain(app.Game.Store.ReadEvents(id, 1), e => e.Type is "TraitSetByDeveloper" or "TraitChanged");
    }

    [Fact]
    public async Task The_panel_redraws_on_its_own_route_and_a_post_from_another_site_is_refused()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);
        await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_a"), ("on", "ship"), ("value", "2")));

        (HttpStatusCode status, string panel) = await Get(client, $"/dev/legacy/{id}/panel");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<section id=\"dev\"", panel);
        Assert.Contains("<td class=\"dev__trait\">trait_a</td>", panel);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/dev/legacy/{id}/trait") { Content = Form(("trait", "trait_b"), ("on", "ship"), ("value", "1")) };
        request.Headers.Add("Origin", "https://elsewhere.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    // 1e part three, Task 5: a pair and a level. The fixture's trait_g sits on a pair; trait_e has levels level_a and level_b; trait_f is hidden.
    [Fact]
    public async Task Setting_a_pair_and_a_level_stores_them_and_the_panel_shows_every_trait()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);

        string pair = await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_g"), ("on", "CREW_02"), ("toward", "CREW_03"), ("value", "2")));
        string level = await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_e"), ("on", "CREW_01"), ("toward", ""), ("value", "level_b")));
        string hidden = await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_f"), ("on", "ship"), ("value", "1")));

        Assert.Contains("trait_g is now 2 on CREW_02 toward CREW_03.", pair);
        Assert.Contains("trait_e is now 2 on CREW_01.", level);
        Assert.Contains("<td class=\"dev__trait\">trait_g</td><td>CREW_02</td><td>CREW_03</td><td class=\"n\">2</td>", hidden);
        Assert.Contains("<td class=\"dev__trait\">trait_e</td><td>CREW_01</td><td></td><td class=\"n\">2 (level_b)</td>", hidden);
        Assert.Contains("<td class=\"dev__trait\">trait_f (hidden)</td>", hidden);
        LoadedLegacy after = (await app.Sessions.Read(app.Live, id))!;
        Assert.Contains(after.State.Module<TraitsState>(TraitsModule.Name).Traits, t => (t.Trait, t.CrewId, t.TargetId, t.Value) == ("trait_g", "CREW_02", "CREW_03", 2));
    }

    [Theory]
    [InlineData("trait_e", "CREW_01", "", "3", "trait_e is set to 0 or one of its levels: level_a (1), level_b (2).")]
    [InlineData("trait_e", "CREW_01", "", "level_c", "trait_e is set to 0 or one of its levels: level_a, level_b.")]
    [InlineData("trait_g", "CREW_02", "", "1", "trait_g sits on a pair: choose whom it points at.")]
    [InlineData("trait_b", "CREW_02", "CREW_03", "1", "trait_b does not sit on a pair, so it points at no one.")]
    [InlineData("trait_g", "CREW_02", "CREW_02", "1", "A pair is two crew members: choose a holder, and another as the target.")]
    [InlineData("trait_b", "CREW_02", "", "6", "trait_b is at most 5.")]
    public async Task A_refused_pair_or_level_says_why_and_changes_nothing(string trait, string on, string toward, string value, string reason)
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY_ONE", 1);

        string body = await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", trait), ("on", on), ("toward", toward), ("value", value)));

        Assert.Contains("<p id=\"dev-result\" class=\"error\" role=\"status\">" + reason, body);
        Assert.DoesNotContain(app.Game.Store.ReadEvents(id, 1), e => e.Type is "TraitSetByDeveloper" or "TraitChanged");
    }

    // Ids and links into the editor are drawn for Dev Play alone: on its page, and on every patch that answers one of its posts.
    [Fact]
    public async Task Dev_play_shows_each_objectives_id_as_a_link_to_its_template_and_keeps_it_on_the_patches()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);

        (_, string dev) = await Get(client, $"/dev/legacy/{id}");
        (_, string play) = await Get(client, $"/legacy/{id}");
        string devPatch = await Post(client, $"/dev/legacy/{id}/advance", new FormUrlEncodedContent(app.Stamped(id, [])));

        Assert.Matches("<td class=\"oid\"><a href=\"/dev/content/objectives/OBJECTIVE_TEMPLATE_[0-9]+\" target=\"_blank\" rel=\"noopener\">o_1_1</a></td>", dev);
        Assert.Contains($"@post('/dev/legacy/{id}/advance'", dev);
        Assert.DoesNotContain("class=\"oid\"", play);
        Assert.Contains($"@post('/legacy/{id}/advance'", play);
        Assert.DoesNotContain("/dev/legacy", play);
        // The briefing the advance answers with is still Dev Play's.
        Assert.Matches("<td class=\"oid\"><a href=\"/dev/content/objectives/", devPatch);
        Assert.Contains($"@post('/dev/legacy/{id}/launch'", devPatch);
    }

    [Fact]
    public async Task Outside_developer_mode_dev_plays_actions_do_not_exist_and_plays_answers_draw_nothing_for_it()
    {
        using var app = new TestApp();
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);

        HttpResponseMessage dev = await client.PostAsync($"/dev/legacy/{id}/advance", new FormUrlEncodedContent(app.Stamped(id, [])));
        string patch = await Post(client, $"/legacy/{id}/advance", new FormUrlEncodedContent(app.Stamped(id, [])));

        Assert.Equal(HttpStatusCode.NotFound, dev.StatusCode);
        Assert.Contains("id=\"main\"", patch);
        Assert.DoesNotContain("class=\"oid\"", patch);
        Assert.DoesNotContain("/dev/", patch);
    }

    // Dev Play's actions are developer pages: a post from another site is refused before it reaches the legacy.
    [Fact]
    public async Task Dev_plays_actions_refuse_a_post_from_another_site()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/dev/legacy/{id}/advance") { Content = new FormUrlEncodedContent(app.Stamped(id, [])) };
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CyclePhase.Orders, (await app.Sessions.Read(app.Live, id))!.State.Phase);
    }

    // A launched mission's route links each encounter, and each authored log line links the page it is written on.
    [Fact]
    public async Task In_dev_play_the_route_and_the_authored_log_lines_link_into_the_editor()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);
        await Post(client, $"/dev/legacy/{id}/advance", new FormUrlEncodedContent(app.Stamped(id, [])));
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        var team = (Sim.Missions.AssignTeam)SimRunner.AutoCaptain.NextCommand(app.Live.Game, legacy.State, app.Live.Pack)!;
        var form = new List<KeyValuePair<string, string>>
        {
            new("objective", team.ObjectiveId), new("lead", team.LeadId), new("priority", "0"), new("autonomy", "0"),
        };
        form.AddRange(team.CrewIds.Select(c => new KeyValuePair<string, string>("crew", c)));

        string launch = await Post(client, $"/dev/legacy/{id}/launch", new FormUrlEncodedContent(app.Stamped(id, form)));
        (_, string page) = await Get(client, $"/dev/legacy/{id}");
        (_, string play) = await Get(client, $"/legacy/{id}");

        Assert.Matches("<span class=\"stop__id\"><a href=\"/dev/content/encounters/[A-Z0-9_]+\" target=\"_blank\" rel=\"noopener\">", launch);
        Assert.Matches("<a class=\"dev-edit\" href=\"/dev/content/(lines|encounters|objectives)/[A-Z0-9_]+\" target=\"_blank\" rel=\"noopener\">Edit line</a>", page);
        Assert.DoesNotContain("/dev/content/", play.Replace("href=\"/dev/content\"", ""));
    }

    // 1e part six, Task 14: "Save as test ship". A legacy played some cycles, with a trait on the ship, a level, and a pair set by
    // hand, is captured; the ship it writes reads, through TestShipSource, as the legacy reads through the module's own source.
    [Fact]
    public async Task Save_as_test_ship_writes_a_ship_that_reads_as_the_legacy_does_and_leaves_the_legacy_alone()
    {
        using var app = new TestApp(developerMode: true, scratch: true);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 3);
        for (int steps = 0; (await app.Sessions.Read(app.Live, id))!.State.Cycle < 4; steps++)
        {
            Assert.True(steps < 400, "The legacy never reached its fourth cycle.");
            await app.Sessions.Run(app.Live, id, legacy =>
            {
                legacy.Send(SimRunner.AutoCaptain.NextCommand(app.Live.Game, legacy.State, app.Live.Pack));
                return legacy;
            });
        }

        LoadedLegacy played = (await app.Sessions.Read(app.Live, id))!;
        IReadOnlyList<string> roster = app.Live.Game.Bus.View<IRosterView>(played.State).Roster;
        await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_a"), ("on", "ship"), ("value", "1")));
        await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_e"), ("on", roster[0]), ("toward", ""), ("value", "level_b")));
        await Post(client, $"/dev/legacy/{id}/trait", Form(("trait", "trait_g"), ("on", roster[1]), ("toward", roster[0]), ("value", "2")));
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        int events = app.Game.Store.ReadEvents(id, 1).Count;
        string stateBefore = System.Text.Json.JsonSerializer.Serialize(legacy.State.Modules);
        ContentPack pack = app.Live.Pack;
        string date = Web.Bridge.Dates.Short(pack.Calendar, legacy.State.Day);
        string shipId = NewIdOf($"LEGACY_ONE on {date}");

        string first = await Post(client, $"/dev/legacy/{id}/testship", Form());
        string second = await Post(client, $"/dev/legacy/{id}/testship", Form());

        Assert.Contains("event: datastar-patch-elements", first);
        Assert.Contains($"Saved LEGACY_ONE on {date} as {shipId} in <a href=\"/dev/content/files/testships\">testships/base.yaml</a>.", first);
        Assert.Contains($"as {shipId}_2 in", second);
        // The legacy is as it was: no event, and the same state.
        LoadedLegacy after = (await app.Sessions.Read(app.Live, id))!;
        Assert.Equal(events, app.Game.Store.ReadEvents(id, 1).Count);
        Assert.Equal(stateBefore, System.Text.Json.JsonSerializer.Serialize(after.State.Modules));

        TestShipsLoadResult ships = ContentLoader.LoadTestShips(app.Editor.Read().Files, pack);
        Assert.Empty(ships.Errors);
        TestShipDef ship = Assert.Single(ships.Ships, s => s.Id == shipId);
        Assert.Contains(ships.Ships, s => s.Id == shipId + "_2");
        Assert.Equal($"LEGACY_ONE on {date}", ship.Name);
        Assert.NotEmpty(ship.History);
        Assert.NotEmpty(ship.Chronicle);
        Assert.Contains(ship.Traits, t => (t.Trait, t.On, t.Toward, t.Value) == ("trait_g", roster[1], roster[0], 2));
        Assert.Contains(ship.Crew, c => c.Morale is not null);

        IStateSource real = Sim.Storylets.LegacySource.Of(app.Live.Game.Bus, after.State);
        var test = new Sim.Storylets.TestShipSource(pack, ship);
        Assert.Equal(real.Cycle, test.Cycle);
        IReadOnlyList<string> living = app.Live.Game.Bus.View<IRosterView>(after.State).Roster;
        Assert.Equal(living, test.Roster);
        foreach (CrewDef crew in pack.Crew)
        {
            Assert.Equal(real.IsAboard(crew.Id), test.IsAboard(crew.Id));
        }

        int[] windows = [1, 2, 6, 30, 120, 100_000];
        foreach (string crew in living)
        {
            Assert.Equal(real.Morale(crew), test.Morale(crew));
            Assert.Equal(real.Loyalty(crew), test.Loyalty(crew));
            Assert.Equal(real.IsFit(crew), test.IsFit(crew));
            Assert.All(pack.Skills, s => Assert.Equal(real.Skill(crew, s.Id), test.Skill(crew, s.Id)));
            foreach (string kind in HistoryKinds.All)
            {
                for (int n = 1; n <= pack.HistoryCycles; n++)
                {
                    Assert.True(real.History(crew, kind, n) == test.History(crew, kind, n), $"{crew} {kind} over {n}");
                }
            }

            Assert.All(ChronicleKinds.All, kind => Assert.All(windows, days => Assert.Equal(real.ChronicleCount(crew, kind, days), test.ChronicleCount(crew, kind, days))));
        }

        foreach (TraitDef trait in pack.Traits)
        {
            Assert.Equal(real.Trait(trait.Id, null), test.Trait(trait.Id, null));
            Assert.Equal(real.TraitSince(trait.Id, null), test.TraitSince(trait.Id, null));
            foreach (string crew in living)
            {
                Assert.True(real.Trait(trait.Id, crew) == test.Trait(trait.Id, crew), $"{trait.Id} on {crew}");
                Assert.Equal(real.TraitSince(trait.Id, crew), test.TraitSince(trait.Id, crew));
                Assert.All(living.Where(o => o != crew), other => Assert.Equal(real.Trait(trait.Id, crew, other), test.Trait(trait.Id, crew, other)));
            }
        }

        Assert.All(pack.Storylets.Select(s => s.Id).Concat(pack.Vignettes.Select(v => v.Id)), s => Assert.Equal(real.StoryletSince(s), test.StoryletSince(s)));
        Assert.All(ChronicleKinds.All, kind => Assert.All(windows, days => Assert.Equal(real.ChronicleCount(kind, days), test.ChronicleCount(kind, days))));
    }

    // A ship the loader would refuse is not written, and the panel says why in the loader's words.
    [Fact]
    public async Task Save_as_test_ship_is_refused_when_the_test_ships_file_has_a_wrong_ship()
    {
        using var app = new TestApp(developerMode: true, scratch: true, prepare: root =>
        {
            string file = Path.Combine(root, "testships", "base.yaml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("cycle: 20", "cycle: 0", StringComparison.Ordinal));
        });
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 1);
        string before = File.ReadAllText(Path.Combine(app.ContentPath, "testships", "base.yaml"));

        string body = await Post(client, $"/dev/legacy/{id}/testship", Form());

        Assert.Contains("<p id=\"dev-ship\" class=\"error\" role=\"status\">Not saved. testships/base.yaml: test_ship_a: cycle: the cycle now starting", body);
        Assert.Equal(before, File.ReadAllText(Path.Combine(app.ContentPath, "testships", "base.yaml")));
    }

    private static string NewIdOf(string name)
    {
        return Web.Forms.NewIds.FromName(name);
    }
}

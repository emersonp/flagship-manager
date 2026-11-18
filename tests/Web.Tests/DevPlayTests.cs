using System.Net;
using Sim.Core;
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
}

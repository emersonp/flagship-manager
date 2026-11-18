using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Storylets;
using Sim.Traits;

namespace Web.Tests;

// Storylets on the captain's itinerary, through the real app on a scratch copy of the fixture. The fixture's storylets are rare in
// play, so each test sets trait_a to 1 as ENCOUNTER_01's partial does, at the end of the first resolution, and STORYLET_01 arrives
// when the second cycle's itinerary opens (1e part two).
public sealed class StoryletBridgeTests : IDisposable
{
    private const string Minus = "−";

    private readonly TestApp app = new(scratch: true);
    private readonly HttpClient client;

    public StoryletBridgeTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private static string Entity(string html)
    {
        return WebUtility.HtmlDecode(html);
    }

    // Plays the first cycle to the end of resolution (sending a team unless told not to), sets trait_a as ENCOUNTER_01 would, then
    // advances through the aftermath into the next itinerary by the page's own route.
    private static async Task<(Guid Id, WebCaptain Captain)> ToItinerary(TestApp app, HttpClient client, bool sendTeam = true, string? flag = "trait_a")
    {
        Guid id = await app.CreateAtOrders("LEGACY", 1);
        Func<LegacyState, ICommand?>? nobody = sendTeam ? null : s => s.Phase == CyclePhase.Briefing ? new AdvancePhase() : null;
        var captain = new WebCaptain(app, client, id, nobody);
        for (int steps = 0; ; steps++)
        {
            Assert.True(steps < 50, "The first cycle never reached the end of resolution.");
            LoadedLegacy legacy = await captain.Read();
            if (legacy.State.Phase == CyclePhase.Resolution && await captain.Peek() is AdvancePhase)
            {
                break;
            }

            await captain.Step();
        }

        if (flag is not null)
        {
            await app.Sessions.Run(app.Live, id, legacy =>
            {
                TraitsState traits = legacy.State.Module<TraitsState>(TraitsModule.Name);
                traits.Traits.RemoveAll(t => t.Trait == flag && t.CrewId is null);
                traits.Traits.Add(new StoredTrait { Trait = flag, CrewId = null, TargetId = null, Value = 1, ChangedAtWeek = legacy.State.Week, EncounterId = "ENCOUNTER_01" });
                return legacy;
            });
        }

        await captain.Post("advance", []);
        await captain.Post("advance", []);
        Assert.Equal(CyclePhase.Itinerary, (await captain.Read()).State.Phase);
        return (id, captain);
    }

    private Task<(Guid Id, WebCaptain Captain)> ToItinerary(bool sendTeam = true, string? flag = "trait_a")
    {
        return ToItinerary(app, client, sendTeam, flag);
    }

    private async Task<string> Offer(Guid id)
    {
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        return legacy.State.Module<StoryletsState>(StoryletsModule.Name).Waiting.Single().OfferId;
    }

    private async Task<string> Page(Guid id)
    {
        return Entity(await client.GetStringAsync($"/legacy/{id}"));
    }

    private async Task<(HttpStatusCode Status, string Body)> Post(Guid id, string route, IEnumerable<KeyValuePair<string, string>> form)
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{id}/{route}", new FormUrlEncodedContent(app.Stamped(id, form)));
        return (response.StatusCode, Entity(await response.Content.ReadAsStringAsync()));
    }

    private static string Matters(string page)
    {
        return Regex.Match(page, "<section id=\"storylets\".*?</section>", RegexOptions.Singleline).Value;
    }

    private static string AdvanceButton(string page)
    {
        return Regex.Match(page, "<form class=\"act\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
    }

    [Fact]
    public async Task A_waiting_storylet_is_shown_with_where_it_came_from_and_a_button_for_each_option()
    {
        (Guid id, _) = await ToItinerary();
        string offer = await Offer(id);

        string page = await Page(id);

        string matters = Matters(page);
        Assert.Contains(">Storylets</h2>", matters);
        Assert.Contains("STORYLET_01", matters);
        Assert.Contains("From ENCOUNTER_01 · trait_a", matters);
        // The script: the captain's line, then the crew member best at SKILL_A among the fit.
        Assert.Matches("Captain</span> <span class=\"script__words\">STORYLET_01_LINE_1", matters);
        Assert.Matches("CREW_01</span> <span class=\"script__words\">STORYLET_01_LINE_2", matters);
        Assert.Equal(2, Regex.Matches(matters, $"<form data-on:submit=\"@post\\('/legacy/{id}/storylet', \\{{contentType: 'form'\\}}\\)\"").Count);
        Assert.Contains($"name=\"offer\" value=\"{offer}\"", matters);
        Assert.Contains("name=\"option\" value=\"option_a\"", matters);
        Assert.Contains("name=\"option\" value=\"option_b\"", matters);
        Assert.Matches("<button[^>]*>STORYLET_01_OPTION_A_LABEL</button>", matters);
        Assert.Matches("<button[^>]*>STORYLET_01_OPTION_B_LABEL</button>", matters);
    }

    [Fact]
    public async Task The_section_is_absent_when_nothing_waits_and_nothing_was_settled()
    {
        (Guid id, _) = await ToItinerary(flag: null);

        string page = await Page(id);

        Assert.Contains("Captain's itinerary", page);
        Assert.DoesNotContain("id=\"storylets\"", page);
        Assert.DoesNotContain("/storylet'", page);
    }

    [Fact]
    public async Task Advance_is_disabled_and_a_posted_advance_is_refused_while_one_waits()
    {
        (Guid id, _) = await ToItinerary();
        int stored = app.Game.Store.ReadEvents(id).Count;

        string advance = AdvanceButton(await Page(id));
        Assert.Matches("<button[^>]*\\sdisabled>To orders</button>", advance);
        Assert.Contains("A storylet is waiting for an answer.", advance);

        (HttpStatusCode status, string body) = await Post(id, "advance", []);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("A storylet is waiting for an answer.", body);
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);
        Assert.Equal(CyclePhase.Itinerary, (await app.Sessions.Read(app.Live, id))!.State.Phase);
    }

    [Fact]
    public async Task Answering_settles_the_panel_with_tags_and_frees_advance()
    {
        (Guid id, _) = await ToItinerary();
        string offer = await Offer(id);

        (HttpStatusCode status, string stream) = await Post(id, "storylet", [new("offer", offer), new("option", "option_a")]);

        Assert.Equal(HttpStatusCode.OK, status);
        string matters = Matters(stream);
        Assert.Contains("STORYLET_01", matters);
        Assert.Contains("STORYLET_01_OPTION_A_TEXT", matters);
        Assert.Contains($"<span class=\"fig-tag\">Loyalty {Minus}1 all crew</span>", matters);
        Assert.Contains("<span class=\"fig-tag\">trait_a 1 to 0</span>", matters);
        Assert.DoesNotContain("<form", matters);
        string advance = AdvanceButton(stream);
        Assert.Contains(">To orders</button>", advance);
        Assert.DoesNotMatch("<button[^>]*\\sdisabled[\\s>]", advance);
        Assert.DoesNotContain("A storylet is waiting for an answer.", advance);
    }

    [Fact]
    public async Task A_reload_after_answering_still_shows_the_settled_panel()
    {
        (Guid id, WebCaptain captain) = await ToItinerary();
        (string route, _) = await captain.Step();
        Assert.Equal("storylet", route);

        string matters = Matters(await Page(id));

        Assert.Contains(">Storylets</h2>", matters);
        Assert.Contains("STORYLET_01_OPTION_A_TEXT", matters);
        Assert.Contains($"Loyalty {Minus}1 all crew", matters);
        Assert.Contains("trait_a 1 to 0", matters);
        Assert.DoesNotContain("<form", matters);
    }

    [Fact]
    public async Task A_second_answer_and_an_unknown_option_answer_422_and_change_nothing()
    {
        (Guid id, _) = await ToItinerary();
        string offer = await Offer(id);
        int stored = app.Game.Store.ReadEvents(id).Count;

        (HttpStatusCode unknown, string unknownBody) = await Post(id, "storylet", [new("offer", offer), new("option", "option_z")]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown);
        Assert.Matches("<p id=\"storylet-refusal\" role=\"alert\">That is not one of its answers\\.</p>", unknownBody);
        // The page is drawn again with the storylet still waiting beside the reason.
        Assert.Contains("STORYLET_01_OPTION_A_LABEL", Matters(unknownBody));
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);

        (HttpStatusCode first, _) = await Post(id, "storylet", [new("offer", offer), new("option", "option_b")]);
        Assert.Equal(HttpStatusCode.OK, first);
        stored = app.Game.Store.ReadEvents(id).Count;

        (HttpStatusCode second, string secondBody) = await Post(id, "storylet", [new("offer", offer), new("option", "option_a")]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second);
        Assert.Matches("<p id=\"storylet-refusal\" role=\"alert\">That storylet is not waiting for an answer\\.</p>", secondBody);
        Assert.Contains("STORYLET_01_OPTION_B_TEXT", Matters(secondBody));
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);

        (HttpStatusCode missing, string missingBody) = await Post(id, "storylet", [new("offer", offer)]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing);
        Assert.Contains("The form was not complete.", missingBody);
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);
    }

    // A Datastar request gets the same patches with 200: Datastar 1.0.5 drops the body of any other status, so the reason would never show.
    [Fact]
    public async Task A_refusal_from_the_page_itself_is_patched_with_200()
    {
        (Guid id, _) = await ToItinerary();
        string offer = await Offer(id);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/legacy/{id}/storylet") { Content = new FormUrlEncodedContent(app.Stamped(id, [new("offer", offer), new("option", "option_z")])) };
        request.Headers.Add("Datastar-Request", "true");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("That is not one of its answers.", Entity(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task A_storylet_waits_on_the_itinerary_after_a_cycle_with_no_team()
    {
        (Guid id, WebCaptain captain) = await ToItinerary(sendTeam: false);

        string page = await Page(id);

        Assert.Contains("STORYLET_01_OPTION_A_LABEL", Matters(page));
        Assert.Matches("<button[^>]*\\sdisabled>To orders</button>", AdvanceButton(page));
        Assert.Equal("storylet", (await captain.Step()).Route);
        Assert.Contains("STORYLET_01_OPTION_A_TEXT", Matters(await Page(id)));
    }

    // A storylet the pack has lost since it was offered blocks nothing and is never drawn; the next itinerary withdraws it.
    [Fact]
    public async Task A_waiting_storylet_the_pack_has_lost_gets_no_panel_and_does_not_block_advance()
    {
        (Guid id, WebCaptain captain) = await ToItinerary(flag: null);
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            legacy.State.Module<StoryletsState>(StoryletsModule.Name).Waiting.Add(new WaitingStorylet { OfferId = "1:STORYLET_GONE", StoryletId = "STORYLET_GONE", Cast = [], EncounterId = "ENCOUNTER_01" });
            return legacy;
        });

        string page = await Page(id);

        Assert.DoesNotContain("id=\"storylets\"", page);
        Assert.DoesNotContain("STORYLET_GONE", page);
        Assert.DoesNotMatch("<button[^>]*\\sdisabled[\\s>]", AdvanceButton(page));
        Assert.DoesNotContain("A storylet is waiting for an answer.", page);
        Assert.Equal("advance", (await captain.Step()).Route);
        Assert.Equal(CyclePhase.Orders, (await captain.Read()).State.Phase);
    }

    // A ship storylet has no subject, so a shared line that names #subject# cannot be said: the next line that holds is used, or,
    // with none, the ids. The page never fails on it.
    [Theory]
    [InlineData(false, "STORYLET_03: option_a, option_b.")]
    [InlineData(true, "PLAIN_SHARED_LINE")]
    public async Task A_ship_storylet_whose_shared_line_names_a_subject_falls_back(bool plainLine, string shown)
    {
        string lines = "- id: LINE_STORYLET_SUBJECT\n  when: { beat: storylet, storylet: STORYLET_03 }\n  text: \"#subject# SUBJECT_SHARED_LINE\"\n"
            + (plainLine ? "- id: LINE_STORYLET_PLAIN\n  when: { beat: storylet }\n  text: PLAIN_SHARED_LINE\n" : "");
        using var sharedApp = new TestApp(scratch: true, prepare: root =>
        {
            File.WriteAllText(Path.Combine(root, "storylets", "base.yaml"), Bare);
            File.AppendAllText(Path.Combine(root, "lines", "base.yaml"), lines);
        });
        HttpClient sharedClient = sharedApp.NewClient();
        (Guid id, _) = await ToItinerary(sharedApp, sharedClient);

        HttpResponseMessage response = await sharedClient.GetAsync($"/legacy/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string script = Regex.Match(Matters(Entity(await response.Content.ReadAsStringAsync())), "<div class=\"script\">.*?</div>", RegexOptions.Singleline).Value;
        Assert.Contains(shown, script);
        Assert.DoesNotContain("SUBJECT_SHARED_LINE", script);
    }

    private const string Bare = "- id: STORYLET_03\n  stakes: 1\n  requires: \"ship.trait.trait_a == 1\"\n  options:\n"
        + "    - { id: option_a, label: BARE_A, effects: [{ trait: trait_a, set: 0 }] }\n"
        + "    - { id: option_b, label: BARE_B, effects: [{ trait: trait_a, set: 2 }] }\n";

    [Fact]
    public async Task A_storylet_with_no_words_shows_its_ids_and_can_be_answered()
    {
        const string bare = "- id: STORYLET_03\n  stakes: 1\n  requires: \"ship.trait.trait_a == 1\"\n  options:\n"
            + "    - { id: option_a, label: BARE_A, effects: [{ trait: trait_a, set: 0 }] }\n"
            + "    - { id: option_b, label: BARE_B, effects: [{ trait: trait_a, set: 2 }] }\n";
        using var bareApp = new TestApp(scratch: true, prepare: root => File.WriteAllText(Path.Combine(root, "storylets", "base.yaml"), bare));
        HttpClient bareClient = bareApp.NewClient();
        (Guid id, WebCaptain captain) = await ToItinerary(bareApp, bareClient);

        string matters = Matters(Entity(await bareClient.GetStringAsync($"/legacy/{id}")));

        Assert.Contains("STORYLET_03", Regex.Match(matters, "<div class=\"script\">.*?</div>", RegexOptions.Singleline).Value);
        Assert.Contains("option_a", matters);
        Assert.Contains("option_b", matters);
        Assert.Matches("<button[^>]*>BARE_A</button>", matters);

        Assert.Equal("storylet", (await captain.Step()).Route);
        string settled = Matters(Entity(await bareClient.GetStringAsync($"/legacy/{id}")));
        Assert.Contains("option_a", Regex.Match(settled, "<div class=\"script\">.*?</div>", RegexOptions.Singleline).Value);
        Assert.Contains("trait_a 1 to 0", settled);
    }
}

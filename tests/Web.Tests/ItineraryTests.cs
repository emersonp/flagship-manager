using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Storylets;
using Sim.Traits;

namespace Web.Tests;

// The captain's itinerary (1e part two), through the real app on a scratch copy of the fixture with its vignettes replaced. The second
// cycle's itinerary follows a cycle in which nobody was sent, so every fit crew member rested: VIGNETTE_B (a choice) arrives for the
// first two on the roster, after VIGNETTE_A (the ship's, with effects), which arrives because trait_a is set; so does STORYLET_01.
public sealed class ItineraryTests : IDisposable
{
    private const string Vignettes = """
        - id: VIGNETTE_A
          scope: ship
          requires:
            - { trait: trait_a, min: 1 }
          text: "best SKILL_A: VIGNETTE_A_LINE"
          effects: [{ morale: 2, on: crew }]
        - id: VIGNETTE_B
          scope: crew
          requires:
            - { last: rested }
          text: "subject: VIGNETTE_B_LINE"
          default: say_nothing
          options:
            - { id: say_something, label: SAY_SOMETHING, effects: [{ morale: 3, on: subject }], text: VIGNETTE_B_SAID }
            - { id: say_nothing, label: SAY_NOTHING, effects: [{ loyalty: -1, on: subject }], text: VIGNETTE_B_NOTHING }

        """;

    private readonly TestApp app = new(scratch: true, prepare: root => File.WriteAllText(Path.Combine(root, "vignettes", "base.yaml"), Vignettes));
    private readonly HttpClient client;

    public ItineraryTests()
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

    private async Task<string> Page(Guid id)
    {
        return Entity(await client.GetStringAsync($"/legacy/{id}"));
    }

    private async Task<(HttpStatusCode Status, string Body)> Post(Guid id, string route, IEnumerable<KeyValuePair<string, string>> form)
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{id}/{route}", new FormUrlEncodedContent(app.Stamped(id, form)));
        return (response.StatusCode, Entity(await response.Content.ReadAsStringAsync()));
    }

    private static string Section(string page, string id)
    {
        return Regex.Match(page, $"<section id=\"{id}\".*?</section>", RegexOptions.Singleline).Value;
    }

    private static string Panel(string page, string title)
    {
        return Regex.Match(Section(page, "vignettes"), $"<article[^>]*>(?:(?!</article>).)*?>{title}</h3>.*?</article>", RegexOptions.Singleline).Value;
    }

    private static string AdvanceButton(string page)
    {
        return Regex.Match(page, "<form class=\"act\"[^>]*>.*?</form>", RegexOptions.Singleline).Value;
    }

    // The first cycle with nobody sent, trait_a set on the ship, then on through the aftermath to the second itinerary.
    private async Task<Guid> ToSecondItinerary()
    {
        Guid id = await app.CreateAtOrders("LEGACY", 1);
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != CyclePhase.Aftermath)
            {
                legacy.Send(new AdvancePhase());
            }

            TraitsState traits = legacy.State.Module<TraitsState>(TraitsModule.Name);
            traits.Traits.Add(new StoredTrait { Trait = "trait_a", CrewId = null, TargetId = null, Value = 1, ChangedAtWeek = legacy.State.Week, EncounterId = "ENCOUNTER_01" });
            return legacy;
        });
        (HttpStatusCode status, _) = await Post(id, "advance", []);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((CyclePhase.Itinerary, 2), await Where(id));
        return id;
    }

    private async Task<(CyclePhase, int)> Where(Guid id)
    {
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        return (legacy.State.Phase, legacy.State.Cycle);
    }

    private async Task<string> Waiting(Guid id, string vignette)
    {
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        return legacy.State.Module<StoryletsState>(StoryletsModule.Name).Vignettes.First(v => v.VignetteId == vignette).OfferId;
    }

    // Review focus 5: the first cycle opens here, with nothing yet to say.
    [Fact]
    public async Task A_new_legacy_opens_on_an_empty_itinerary_with_captains_time()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);

        string page = await Page(id);

        Assert.Contains("<h1>Captain's itinerary</h1>", page);
        Assert.Contains("Nothing needs the captain this week.", page);
        Assert.Matches("<p class=\"itinerary__time\" id=\"captains-time\"><span>Captain's time</span><span class=\"m\">3</span><span class=\"note\">Not spent yet</span></p>", page);
        Assert.DoesNotContain("id=\"storylets\"", page);
        Assert.DoesNotContain("id=\"vignettes\"", page);
        Assert.Matches("aria-current=\"step\"[^>]*><span class=\"phase__no\">1</span><span class=\"phase__name\">Itinerary</span>", page);
        string advance = AdvanceButton(page);
        Assert.Contains(">To orders</button>", advance);
        Assert.Contains("name=\"phase\" value=\"Itinerary\"", advance);

        await Post(id, "advance", []);

        Assert.Equal((CyclePhase.Orders, 1), await Where(id));
    }

    [Fact]
    public async Task A_waiting_storylet_and_the_vignettes_are_shown_and_the_storylet_blocks_to_orders()
    {
        Guid id = await ToSecondItinerary();

        string page = await Page(id);

        Assert.Contains("One storylet needs an answer before the orders.", page);
        Assert.Contains("STORYLET_01_OPTION_A_LABEL", Section(page, "storylets"));
        string vignettes = Section(page, "vignettes");
        Assert.Equal(3, Regex.Matches(vignettes, "<article class=\"storylet vignette\"").Count);
        Assert.True(vignettes.IndexOf("VIGNETTE_A", StringComparison.Ordinal) < vignettes.IndexOf("VIGNETTE_B", StringComparison.Ordinal));
        Assert.Matches("<p class=\"note\">CREW_01, rested</p>", vignettes);
        Assert.Contains("Left alone: SAY_NOTHING", vignettes);
        Assert.Matches("<button[^>]*\\sdisabled>To orders</button>", AdvanceButton(page));
        Assert.Contains("A storylet is waiting for an answer.", AdvanceButton(page));
    }

    [Fact]
    public async Task A_vignette_with_effects_is_settled_as_it_arrives_and_shows_its_tags()
    {
        Guid id = await ToSecondItinerary();

        string panel = Panel(await Page(id), "VIGNETTE_A");

        Assert.Contains("VIGNETTE_A_LINE", panel);
        Assert.Contains("<span class=\"fig-tag\">Morale +2 all crew</span>", panel);
        Assert.DoesNotContain("<form", panel);
    }

    [Fact]
    public async Task Storylets_and_vignettes_can_be_answered_in_any_order()
    {
        Guid id = await ToSecondItinerary();
        string vignette = await Waiting(id, "VIGNETTE_B");

        (HttpStatusCode status, string stream) = await Post(id, "vignette", [new("offer", vignette), new("option", "say_something")]);

        Assert.Equal(HttpStatusCode.OK, status);
        string answered = Regex.Match(Section(stream, "vignettes"), "<article[^>]*>(?:(?!</article>).)*?CREW_01, rested.*?</article>", RegexOptions.Singleline).Value;
        Assert.Contains("VIGNETTE_B_SAID", answered);
        Assert.Contains("<span class=\"fig-tag\">Morale +3 CREW_01</span>", answered);
        Assert.Matches("<button[^>]*\\sdisabled>To orders</button>", AdvanceButton(stream));

        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        string storylet = legacy.State.Module<StoryletsState>(StoryletsModule.Name).Waiting.Single().OfferId;
        (status, stream) = await Post(id, "storylet", [new("offer", storylet), new("option", "option_a")]);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotMatch("<button[^>]*\\sdisabled[\\s>]", AdvanceButton(stream));
        Assert.Contains("Nothing must be answered before the orders. One vignette may be.", stream);
    }

    // Review focus 4.
    [Fact]
    public async Task Leaving_applies_each_unanswered_default_once_and_never_an_answered_ones()
    {
        Guid id = await ToSecondItinerary();
        string first = await Waiting(id, "VIGNETTE_B");
        await Post(id, "vignette", [new("offer", first), new("option", "say_something")]);
        LoadedLegacy before = (await app.Sessions.Read(app.Live, id))!;
        await Post(id, "storylet", [new("offer", before.State.Module<StoryletsState>(StoryletsModule.Name).Waiting.Single().OfferId), new("option", "option_a")]);
        await Page(id);
        int stored = app.Game.Store.ReadEvents(id).Count;

        (HttpStatusCode status, _) = await Post(id, "advance", []);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal((CyclePhase.Orders, 2), await Where(id));
        List<VignetteAnswered> answers = app.Game.Store.ReadEvents(id).Skip(stored)
            .Select(e => app.Live.Game.Events.FromEnvelope(e)).OfType<VignetteAnswered>().ToList();
        VignetteAnswered left = Assert.Single(answers);
        Assert.Equal(("say_nothing", true), (left.OptionId, left.Defaulted));
        Assert.NotEqual(first, left.OfferId);
        LoadedLegacy after = (await app.Sessions.Read(app.Live, id))!;
        Assert.Empty(after.State.Module<StoryletsState>(StoryletsModule.Name).Vignettes);
    }

    [Fact]
    public async Task A_reload_after_answering_a_vignette_shows_it_settled_and_a_second_answer_answers_422()
    {
        Guid id = await ToSecondItinerary();
        string offer = await Waiting(id, "VIGNETTE_B");
        await Post(id, "vignette", [new("offer", offer), new("option", "say_nothing")]);
        int stored = app.Game.Store.ReadEvents(id).Count;

        string page = await Page(id);
        (HttpStatusCode status, string body) = await Post(id, "vignette", [new("offer", offer), new("option", "say_something")]);
        (HttpStatusCode unknown, string unknownBody) = await Post(id, "vignette", [new("offer", await Waiting(id, "VIGNETTE_B")), new("option", "shout")]);

        Assert.Contains("VIGNETTE_B_NOTHING", Section(page, "vignettes"));
        Assert.Contains($"Loyalty {"−"}1 CREW_01", Section(page, "vignettes"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Matches("<p id=\"storylet-refusal\" role=\"alert\">That vignette is not waiting for an answer\\.</p>", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown);
        Assert.Contains("That is not one of its answers.", unknownBody);
        Assert.Equal(stored, app.Game.Store.ReadEvents(id).Count);
    }
}

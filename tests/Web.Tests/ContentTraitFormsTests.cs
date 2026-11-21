using System.Net;
using System.Text.RegularExpressions;
using Sim.Content;
using Web.Forms;
using static Web.Tests.ContentFormsTests;
using static Web.Tests.ContentMissionFormsTests;

namespace Web.Tests;

// The trait form and "Where it's used" (1e part six, Task 12), through the real app on a scratch copy of the content: every trait opens
// and saves unchanged, a new one is written and saved, a pair's parts are refused off a pair, and where each trait is used is listed.
public sealed class ContentTraitFormsTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    // A new trait on the fixture, with no id: a counting trait on crew, capped, easing each week, with a launch rule.
    private const string NewTrait =
        "id=&name=Rattled nerves&on=crew&shown=true&values=max&max=4&everyWeeks=2&everyAdd=-1&expires=&briefing=Rattled&missionBonus=&missionWithTarget=&notes="
        + "&sent[0].what=morale&sent[0].amount=-2&sent[0].on="
        + "&notSent[0].what=trait&notSent[0].trait=&notSent[0].mode=add&notSent[0].amount=-1&notSent[0].on=";

    private static string Url(string id) => $"/dev/content/traits/{id}";

    private static string Errors(string body)
    {
        return Regex.Match(body, "class=\"alert\".*?</div>", RegexOptions.Singleline).Value + " " + string.Join(" | ", Regex.Matches(body, "<p class=\"error\"[^>]*>([^<]*)</p>").Select(m => m.Groups[1].Value));
    }

    // --- Nothing the file holds is lost ---

    [Fact]
    public async Task Every_trait_in_the_games_pack_opens_and_saving_it_unchanged_leaves_its_file_byte_for_byte_the_same()
    {
        using TestApp app = ContentStoryletFormsTests.GameApp(canonical: false);
        HttpClient client = app.NewClient();
        string pack = PackJson(app);
        string before = Everything(app);
        var items = app.Editor.Read().Index().Where(i => i.Kind == ContentKind.Trait).ToList();
        Assert.Equal(7, items.Count);

        foreach (ContentItemRef item in items)
        {
            (HttpStatusCode opened, string body) = await Get(client, Url(item.Id));
            Assert.Equal(HttpStatusCode.OK, opened);
            (HttpStatusCode status, string saved) = await Post(client, Url(item.Id), FormFields(body));
            Assert.True(status == HttpStatusCode.OK, $"{item.Id}: {status} {Errors(saved)}");
        }

        Assert.Equal(before, Everything(app));
        Assert.Equal(pack, PackJson(app));
    }

    [Fact]
    public async Task Every_trait_in_the_fixture_opens_and_saving_it_unchanged_leaves_its_file_as_it_was()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string pack = PackJson(app);
        string before = Everything(app);

        foreach (ContentItemRef item in app.Editor.Read().Index().Where(i => i.Kind == ContentKind.Trait).ToList())
        {
            (_, string body) = await Get(client, Url(item.Id));
            (HttpStatusCode status, string saved) = await Post(client, Url(item.Id), FormFields(body));
            Assert.True(status == HttpStatusCode.OK, $"{item.Id}: {status} {Errors(saved)}");
        }

        // Pairs with onTargetDied changing another trait, hidden ones, named levels with launch rules, and bare ids all came back.
        Assert.Equal(before, Everything(app));
        Assert.Equal(pack, PackJson(app));
    }

    // --- Writing a new one ---

    [Fact]
    public async Task A_new_trait_with_no_id_takes_one_from_its_name_and_saves_its_rules()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/traits", NewTrait);

        Assert.True(status == HttpStatusCode.OK, Errors(body));
        Assert.Contains(
            "- id: rattled_nerves\n  name: Rattled nerves\n  on: crew\n  max: 4\n  briefing: Rattled\n  launch:\n    sent: [{ morale: -2 }]\n    notSent: [{ add: -1 }]\n  every: { weeks: 2, add: -1 }\n",
            Disk(app, "traits/base.yaml"),
            StringComparison.Ordinal);
        Assert.Contains(app.Live.Pack.Traits, t => t.Id == "rattled_nerves" && t.Max == 4 && t.Launch!.Sent.Count == 1);
        Assert.Contains("Rattled nerves", (await Get(client, "/dev/content/traits")).Body);
    }

    [Fact]
    public async Task Named_levels_save_by_name_and_a_rule_sets_one_by_name()
    {
        using TestApp app = NewApp();

        string levels = Merge(NewTrait, "values=levels&levels[0].number=1&levels[0].name=uneasy&levels[1].number=2&levels[1].name=rattled&everyWeeks=&everyAdd="
            + "&notSent[0].mode=set&notSent[0].amount=uneasy");
        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/traits", levels);

        Assert.True(status == HttpStatusCode.OK, Errors(body));
        string disk = Disk(app, "traits/base.yaml");
        Assert.Contains("  levels: { 1: uneasy, 2: rattled }\n", disk, StringComparison.Ordinal);
        Assert.Contains("    notSent: [{ set: uneasy }]\n", disk, StringComparison.Ordinal);
        // The max is a number's; named levels are one or the other.
        Assert.DoesNotContain("  max: 4\n  briefing: Rattled", disk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pairs_parts_are_refused_on_a_trait_that_is_not_a_pair()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/traits", Merge(NewTrait, "onTargetDied[0].what=morale&onTargetDied[0].amount=-5&onTargetDied[0].on=&sent[0].on=target"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains(TraitKind.PairRule, body);
        Assert.Contains("Only a trait on a pair has a target.", body);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task The_form_offers_one_of_levels_or_max_and_shows_a_pairs_parts_only_for_a_pair()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        string counting = (await Get(client, Url("trait_b"))).Body;
        Assert.Equal("max", Selected(counting, "values"));
        Assert.Contains("id=\"f-max\"", counting);
        Assert.DoesNotContain("id=\"f-levels-0-name\"", counting);
        Assert.DoesNotContain("id=\"f-onTargetDied-rows\"", counting);

        string named = (await Get(client, Url("trait_e"))).Body;
        Assert.Equal("levels", Selected(named, "values"));
        Assert.Equal("level_b", Value(named, "levels[1].name"));
        Assert.DoesNotContain("id=\"f-max\"", named);
        Assert.Equal("level_b", Selected(named, "sent[0].amount"));

        string pair = (await Get(client, Url("trait_h"))).Body;
        Assert.Contains("id=\"f-onTargetDied-rows\"", pair);
        Assert.Equal("trait_j", Selected(pair, "onTargetDied[1].trait"));
    }

    // Choosing named levels posts the form, which comes back with a first level to name.
    [Fact]
    public async Task Switching_to_named_levels_starts_a_first_level()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/traits/rows?focus=f-values", Merge(NewTrait, "values=levels"), datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Matches("name=\"levels\\[0\\]\\.number\"[^>]*value=\"1\"", body);
    }

    // --- Where it's used ---

    [Fact]
    public async Task Grieving_shows_the_storylet_that_sets_it_and_the_one_that_reads_it()
    {
        using TestApp app = ContentStoryletFormsTests.GameApp(canonical: false);

        string body = WebUtility.HtmlDecode((await Get(app.NewClient(), Url("grieving"))).Body);

        string uses = Regex.Match(body, "<section class=\"plays\" id=\"uses\".*?</section>", RegexOptions.Singleline).Value;
        Assert.Contains("Where it's used", uses);
        Assert.Contains("href=\"/dev/content/storylets/STORYLET_08\">The names on the wall</a>", uses);
        Assert.Contains("Not now: sets 1 on voice", uses);
        // Part six's worked example reads it, so the warning that nothing does is gone.
        Assert.Contains("href=\"/dev/content/storylets/STORYLET_09\">Time for it later</a>", uses);
        Assert.Contains("reads: subject.trait.grieving >= 1", uses);
        Assert.Contains("Hear the names now: sets 0 on subject", uses);
        Assert.DoesNotContain("nothing reads it", uses);
        Assert.DoesNotContain("id=\"uses-vignettes\"", uses);
    }

    [Fact]
    public void Where_a_trait_is_used_lists_reads_by_clause_changes_and_test_ships_by_kind()
    {
        ContentFileSet files = ContentFileSet.ReadDirectory(Path.Combine(TestApp.RepositoryRoot(), "content"));

        TraitUses shaken = TraitUses.Of(files, "shaken");
        Assert.True(shaken.Loaded);
        Assert.Equal(["Storylets", "Encounters", "Test ships"], shaken.Groups.Select(g => g.Title));
        TraitUse someone = shaken.Groups[0].Uses.Single(u => u.Item.Id == "STORYLET_02");
        Assert.Contains("reads: subject.trait.shaken >= 1", someone.Says);
        Assert.Contains(shaken.Groups.Single(g => g.Kind == ContentKind.Encounter).Uses.SelectMany(u => u.Says), s => s.EndsWith(": adds 1 on the actor", StringComparison.Ordinal));
        Assert.Contains("holds 2 on CREW_02", shaken.Groups.Single(g => g.Kind == ContentKind.TestShip).Uses.Single().Says);
        Assert.Empty(shaken.Warnings);

        // An encounter's requirement is a read too, and the ship is where the sensor quirk sits.
        TraitUses quirk = TraitUses.Of(files, "sensors_quirk");
        Assert.Contains(quirk.Groups.Single(g => g.Kind == ContentKind.Encounter).Uses.SelectMany(u => u.Says), s => s.StartsWith("reads: sensors_quirk at most 1, on the ship, for ", StringComparison.Ordinal));

        // The traits list's "Used by" counts the items listed.
        Assert.Equal(shaken.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), ContentKinds.Cells(files, files.Index().Single(i => i.Kind == ContentKind.Trait && i.Id == "shaken"))[3]);
    }

    [Fact]
    public void A_trait_read_and_never_set_is_warned_of()
    {
        ContentFileSet game = ContentFileSet.ReadDirectory(Path.Combine(TestApp.RepositoryRoot(), "content"));
        string storylets = game.Files["storylets/base.yaml"].Replace("subject.trait.shaken >= 1 && ", "subject.trait.grieving >= 1 && subject.trait.shaken >= 1 && ", StringComparison.Ordinal);
        string without = storylets.Replace("{ trait: grieving, set: 1, on: voice }", "{ morale: -1, on: voice }", StringComparison.Ordinal);
        var files = new ContentFileSet(new Dictionary<string, string>(game.Files) { ["storylets/base.yaml"] = without });
        Assert.NotNull(files.Validate().Pack);

        TraitUses grieving = TraitUses.Of(files, "grieving");

        Assert.Equal(["grieving is read, but nothing in the pack sets it, so what reads it sees 0."], grieving.Warnings);
    }

    // The loader judges the save: a level a storylet reads by name cannot be renamed under it.
    [Fact]
    public async Task Renaming_a_level_a_storylet_reads_is_refused_by_the_loader_at_the_storylet()
    {
        using TestApp app = ContentStoryletFormsTests.GameApp(canonical: false);
        HttpClient client = app.NewClient();
        string before = Everything(app);
        string body = (await Get(client, Url("came_back"))).Body;
        Assert.Equal("withdrawn", Value(body, "levels[1].name"));

        (HttpStatusCode status, string refused) = await Post(client, Url("came_back"), Merge(FormFields(body), "levels[1].name=withdrew"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        string alert = WebUtility.HtmlDecode(Regex.Match(refused, "id=\"form-alert\".*?</div>", RegexOptions.Singleline).Value);
        Assert.Contains("The change was refused. Nothing was written.", alert);
        Assert.Contains("storylets/base.yaml", alert);
        Assert.Contains("STORYLET_06", alert);
        Assert.Contains("withdrawn", alert);
        Assert.Equal(before, Everything(app));
    }

    // The fixture is the editor's to rewrite; the repository's content is not touched.
    [Fact]
    public async Task The_repositorys_own_content_is_byte_identical_after_trait_saves()
    {
        using TestApp app = NewApp();
        await Post(app.NewClient(), "/dev/content/traits", NewTrait);

        guard.Verify();
    }
}

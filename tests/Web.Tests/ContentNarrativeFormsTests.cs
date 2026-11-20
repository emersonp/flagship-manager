using System.Net;
using Sim.Content;
using Sim.Core.Content;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// The editor's forms for lines, symbols, and pronoun sets, and the crew's pronoun pick (slice 1d), through the real app on a scratch copy.
public sealed class ContentNarrativeFormsTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private static string Esc(string fields) => string.Join("&", Fields(fields).Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

    private static ContentPack Live(TestApp app) => ContentLoader.LoadDirectory(app.ContentPath).Pack!;

    [Fact]
    public async Task A_new_line_is_written_with_its_criteria_and_is_live_at_once()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/lines", "id=LINE_NEW&beat=check&when[0].fact=tier&when[0].value=failure, disaster&when[1].fact=actor.caution&when[1].value=1..&text=" + Uri.EscapeDataString("#actor# hesitates at #encounter#."));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Saved.", body, StringComparison.Ordinal);
        Assert.Contains("- id: LINE_NEW\n  when: { beat: check, tier: [failure, disaster], actor.caution: { min: 1 } }\n  text: \"#actor# hesitates at #encounter#.\"\n", Disk(app, "lines/base.yaml"), StringComparison.Ordinal);
        LineDef line = Live(app).Lines.Single(l => l.Id == "LINE_NEW");
        Assert.Equal(3, line.Specificity);
        Assert.Equal(new CriterionDef("actor.caution", [], 1, null), line.When[1] with { Values = [] });
        Assert.Contains(app.Live.Pack.Lines, l => l.Id == "LINE_NEW");
        guard.Verify();
    }

    [Fact]
    public async Task An_existing_line_opens_with_its_values_as_text_and_saves_unchanged()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();
        string before = Disk(app, "lines/base.yaml");

        (_, string form) = await Get(client, "/dev/content/lines/LINE_CHECK_BAD");
        Assert.Equal("failure, disaster", Value(form, "when[0].value"));
        Assert.DoesNotContain("name=\"use\"", form, StringComparison.Ordinal);
        (HttpStatusCode status, _) = await Post(client, "/dev/content/lines/LINE_CHECK_BAD", "beat=check&when[0].fact=tier&when[0].value=failure, disaster&text=" + Uri.EscapeDataString("At #encounter#, #actor# failed at #skill#."));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(before, Disk(app, "lines/base.yaml"));
    }

    [Theory]
    [InlineData("beat=stage&when[0].fact=tier&when[0].value=failure&text=x", "when[0].fact", "A stage beat has no fact &#x27;tier&#x27;.")]
    [InlineData("beat=check&when[0].fact=margin&when[0].value=3..1&text=x", "when[0].value", "The range&#x27;s first number is above its last.")]
    [InlineData("beat=check&when[0].fact=tier&when[0].value=a,,b&text=x", "when[0].value", "Leave no blank between commas.")]
    [InlineData("beat=&text=x", "beat", "Choose a beat.")]
    [InlineData("beat=check&text=", "text", "Enter the line&#x27;s text.")]
    public async Task A_line_the_form_can_judge_is_refused_beside_its_field(string fields, string field, string message)
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/lines", "id=LINE_BAD&" + fields);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains(message, body, StringComparison.Ordinal);
        Assert.Contains($"id=\"{Web.Forms.FormValues.Slug(field)}\"", body, StringComparison.Ordinal);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_line_the_loader_refuses_shows_the_loaders_reason_and_writes_nothing()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/lines", "id=LINE_BAD&beat=stage&text=" + Uri.EscapeDataString("#actor# arrives."));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("The change was refused. Nothing was written.", body, StringComparison.Ordinal);
        Assert.Contains("a stage beat does not have", body, StringComparison.Ordinal);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task The_preview_expands_the_posted_line_against_sample_beats_and_says_whether_it_wins()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/lines/rows", "id=LINE_TRY&beat=check&when[0].fact=helped&when[0].value=true&when[1].fact=tier&when[1].value=critical, success, partial, failure, disaster&text=" + Uri.EscapeDataString("TRY #actor# with #helper#."), datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        string text = WebUtility.HtmlDecode(body);
        Assert.Contains("sample beats match.", text, StringComparison.Ordinal);
        Assert.Matches("<p class=\"preview__text\">TRY [^<]+ with [^<]+\\.</p>", text);
        Assert.Contains("This line wins.", text, StringComparison.Ordinal);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task The_preview_expands_a_chronicle_line_against_the_chronicles_samples()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/lines/rows", "id=LINE_TRY&beat=chronicle_death&when[0].fact=dead.isLead&when[0].value=true&text=" + Uri.EscapeDataString("TRY #dead# at the #encounter#, after #cycles#."), datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        string text = WebUtility.HtmlDecode(body);
        Assert.Contains("Chronicle sample: The lead's death on a check", text, StringComparison.Ordinal);
        Assert.Matches("<p class=\"preview__text\">TRY [^<]+ at the [^<]+, after 8 cycles\\.</p>", text);
        Assert.Contains("This line wins.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_preview_names_the_line_that_beats_a_generic_one()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (_, string body) = await Post(client, "/dev/content/lines/LINE_CHECK/rows", "beat=check&text=" + Uri.EscapeDataString("#actor# works."), datastar: true);

        string text = WebUtility.HtmlDecode(body);
        Assert.Contains("LINE_CHECK_HELPED wins instead.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_id_hint_quotes_the_rule_for_the_kind()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (_, string symbol) = await Get(client, "/dev/content/symbols/new");
        (_, string skill) = await Get(client, "/dev/content/skills/new");

        Assert.Contains(Ids.InnerRule, symbol, StringComparison.Ordinal);
        Assert.DoesNotContain(Ids.ItemRule, symbol, StringComparison.Ordinal);
        Assert.Contains(Ids.ItemRule, skill, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_lines_page_shows_the_lint_and_other_kinds_do_not()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (_, string lines) = await Get(client, "/dev/content/lines");
        (_, string skills) = await Get(client, "/dev/content/skills");

        Assert.Contains("<section class=\"well\" aria-labelledby=\"h-lint\" id=\"lint\">", lines, StringComparison.Ordinal);
        Assert.Contains("beats of sample missions", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"lint\"", skills, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_symbol_is_created_with_a_lower_case_id_and_its_options()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (HttpStatusCode refused, string body) = await Post(client, "/dev/content/symbols", "id=Hesitates&options[0].text=WORD_A");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused);
        Assert.Contains(Ids.InnerRule, body, StringComparison.Ordinal);

        (HttpStatusCode status, _) = await Post(client, "/dev/content/symbols", Esc("id=hesitates&options[0].text=WORD_A&options[1].text=#actor.they# pause"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("- id: hesitates\n  options:\n    - WORD_A\n    - \"#actor.they# pause\"\n", Disk(app, "symbols/base.yaml"));
        Assert.Equal(["WORD_A", "#actor.they# pause"], Live(app).Symbols.Single().Options);
    }

    [Fact]
    public async Task A_pronoun_set_is_created_and_crew_can_pick_it()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();

        (HttpStatusCode created, _) = await Post(client, "/dev/content/pronouns", "id=xe&subject=xe&object=xem&possessive=xyr&possessivePronoun=xyrs&reflexive=xemself");
        Assert.Equal(HttpStatusCode.OK, created);
        (_, string crewForm) = await Get(client, "/dev/content/crew/CREW_01");
        Assert.Contains("<option value=\"xe\"", crewForm, StringComparison.Ordinal);

        string crew = "name=&seniority=5&caution=2&discipline=2&skills.SKILL_A=6&skills.SKILL_B=5&skills.SKILL_C=4&skills.SKILL_D=3&pronouns=xe";
        (HttpStatusCode saved, _) = await Post(client, "/dev/content/crew/CREW_01", crew);

        Assert.Equal(HttpStatusCode.OK, saved);
        Assert.Contains("  pronouns: xe\n", Disk(app, "crew/starting.yaml"), StringComparison.Ordinal);
        ContentPack pack = Live(app);
        Assert.Equal("xem", pack.PronounsOf(pack.Crew.Single(c => c.Id == "CREW_01")).Object);
        Assert.False(pack.PronounsOf(pack.Crew.Single(c => c.Id == "CREW_01")).Plural);
    }

    [Fact]
    public async Task A_pronoun_set_crew_use_cannot_be_deleted_and_a_bad_pick_is_refused()
    {
        using TestApp app = NewApp();
        using HttpClient client = app.CreateClient();
        string crew = "name=&seniority=5&caution=2&discipline=2&skills.SKILL_A=6&skills.SKILL_B=5&skills.SKILL_C=4&skills.SKILL_D=3";
        (HttpStatusCode bad, string body) = await Post(client, "/dev/content/crew/CREW_01", crew + "&pronouns=nope");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad);
        Assert.Contains("Choose a pronoun set the pack has.", body, StringComparison.Ordinal);

        await Post(client, "/dev/content/crew/CREW_01", crew + "&pronouns=she");
        string before = Everything(app);
        (_, string refused) = await Post(client, "/dev/content/pronouns/she/delete", "");

        Assert.Contains("unknown pronoun set &#x27;she&#x27;", refused, StringComparison.Ordinal);
        Assert.Equal(before, Everything(app));
    }
}

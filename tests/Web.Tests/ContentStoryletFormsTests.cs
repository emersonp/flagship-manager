using System.Net;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core.Content;
using Web.Forms;
using static Web.Tests.ContentFormsTests;
using static Web.Tests.ContentMissionFormsTests;

namespace Web.Tests;

// The storylet form (1e part six, Task 8), through the real app on a scratch copy of the content: every storylet opens and saves
// unchanged, a new one is written from rows and saved, options keep their ids as they move, and the loader's refusals go to their fields.
public sealed class ContentStoryletFormsTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    // A new storylet on the fixture: one slot from rows (subject's trait_b is at least 1), a script, and two options settled by their
    // effects, each of which clears trait_b, so neither leaves the storylet able to come straight back.
    private const string NewStorylet =
        "id=STORYLET_90&name=NAME_PLACEHOLDER_NEW&stakes=2&requires.mode=rows"
        + "&cast[0].name=subject&cast[0].when.mode=rows"
        + "&cast[0].when[0].kind=Trait&cast[0].when[0].who=subject&cast[0].when[0].name=trait_b&cast[0].when[0].op=>%3D&cast[0].when[0].value=1"
        + "&cooldownCast=&cooldownGlobal=&chronicleWeight=&script=subject: STORYLET_90_LINE_1%0D%0A%3D %23subject%23 STORYLET_90_REPORT&notes="
        + "&options[0].id=&options[0].label=Hold a service&options[0].settled=effects&options[0].requires.mode=rows"
        + "&options[0].effects[0].what=trait&options[0].effects[0].trait=trait_b&options[0].effects[0].mode=set&options[0].effects[0].amount=0&options[0].effects[0].on=&options[0].effects[0].toward="
        + "&options[0].effects[1].what=loyalty&options[0].effects[1].amount=1&options[0].effects[1].on=subject"
        + "&options[0].text=%23subject%23 STORYLET_90_OPTION_A_TEXT"
        + "&options[1].id=option_b&options[1].label=STORYLET_90_OPTION_B_LABEL&options[1].settled=effects&options[1].requires.mode=rows"
        + "&options[1].effects[0].what=trait&options[1].effects[0].trait=trait_b&options[1].effects[0].mode=add&options[1].effects[0].amount=-1"
        + "&options[1].text=STORYLET_90_OPTION_B_TEXT";

    // The game's own content in place of the fixture's, with its storylets file written as the writer writes it, so a save of an
    // unchanged storylet can be compared byte for byte (a first save turns the file's flow-style options into blocks).
    private static TestApp GameApp(bool canonical)
    {
        return NewApp(root =>
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(root))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, true);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            string source = Path.Combine(TestApp.RepositoryRoot(), "content");
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            if (canonical)
            {
                ContentFileSet files = ContentEdits.Canonicalize(ContentFileSet.ReadDirectory(root));
                File.WriteAllText(Path.Combine(root, "storylets", "base.yaml"), files.Files["storylets/base.yaml"]);
            }
        });
    }

    private static string Url(string id) => $"/dev/content/storylets/{id}";

    // --- Nothing the file holds is lost ---

    [Fact]
    public async Task Every_storylet_in_the_games_pack_opens_and_saving_it_unchanged_leaves_its_file_byte_for_byte_the_same()
    {
        string pack;
        using (TestApp original = GameApp(canonical: false))
        {
            pack = PackJson(original);
        }

        using TestApp app = GameApp(canonical: true);
        HttpClient client = app.NewClient();
        Assert.Equal(pack, PackJson(app));
        string before = Everything(app);
        var items = app.Editor.Read().Index().Where(i => i.Kind == ContentKind.Storylet).ToList();
        Assert.Equal(8, items.Count);

        foreach (ContentItemRef item in items)
        {
            (HttpStatusCode opened, string body) = await Get(client, Url(item.Id));
            Assert.Equal(HttpStatusCode.OK, opened);
            (HttpStatusCode status, string saved) = await Post(client, Url(item.Id), FormFields(body));
            Assert.True(status == HttpStatusCode.OK, $"{item.Id}: {status} {Regex.Match(saved, "class=\"alert\".*?</div>", RegexOptions.Singleline).Value} {string.Join(" | ", Regex.Matches(saved, "<p class=\"error\"[^>]*>([^<]*)</p>").Select(m => m.Groups[1].Value))}");
        }

        Assert.Equal(before, Everything(app));
        Assert.Equal(pack, PackJson(app));
    }

    [Fact]
    public async Task Every_storylet_in_the_fixture_opens_and_saving_it_unchanged_leaves_the_pack_as_it_was()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string pack = PackJson(app);

        foreach (ContentItemRef item in app.Editor.Read().Index().Where(i => i.Kind == ContentKind.Storylet).ToList())
        {
            (_, string body) = await Get(client, Url(item.Id));
            (HttpStatusCode status, _) = await Post(client, Url(item.Id), FormFields(body));
            Assert.Equal(HttpStatusCode.OK, status);
        }

        Assert.Equal(pack, PackJson(app));
        // The check option, the option with requires, the pair's toward, a level set by name, and the notes-free file all came back.
        string disk = Disk(app, "storylets/base.yaml");
        Assert.Contains("    - id: option_a\n      label: STORYLET_04_OPTION_A_LABEL\n      check: { skill: SKILL_B, difficulty: 4 }\n      success:\n        effects: [{ trait: trait_h, set: 0, toward: target }, { morale: 2, on: handler }]\n", disk, StringComparison.Ordinal);
        Assert.Contains("requires: \"subject.loyalty >= 60\"", disk, StringComparison.Ordinal);
        Assert.Contains("{ trait: trait_e, set: level_a }", disk, StringComparison.Ordinal);
    }

    // --- Writing a new one ---

    [Fact]
    public async Task A_new_storylet_with_a_slot_from_rows_and_effects_options_saves_appears_on_the_list_and_loads()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/storylets", NewStorylet);

        Assert.True(status == HttpStatusCode.OK, Regex.Match(body, "class=\"alert\".*?</div>", RegexOptions.Singleline).Value);
        string disk = Disk(app, "storylets/base.yaml");
        Assert.Contains(
            "- id: STORYLET_90\n  name: NAME_PLACEHOLDER_NEW\n  stakes: 2\n  cast:\n    subject: \"subject.trait.trait_b >= 1\"\n  text: |-\n    subject: STORYLET_90_LINE_1\n    = #subject# STORYLET_90_REPORT\n  options:\n"
            + "    - id: hold_service\n      label: Hold a service\n      effects: [{ trait: trait_b, set: 0 }, { loyalty: 1, on: subject }]\n      text: \"#subject# STORYLET_90_OPTION_A_TEXT\"\n"
            + "    - id: option_b\n      label: STORYLET_90_OPTION_B_LABEL\n      effects: [{ trait: trait_b, add: -1 }]\n      text: STORYLET_90_OPTION_B_TEXT\n",
            disk,
            StringComparison.Ordinal);
        Assert.Contains(app.Live.Pack.Storylets, s => s.Id == "STORYLET_90" && s.Options.Select(o => o.Id).SequenceEqual(["hold_service", "option_b"]));
        Assert.Contains("NAME_PLACEHOLDER_NEW", (await Get(client, "/dev/content/storylets")).Body);

        // It opens again as it was written: the slot as a row, and each option's id fixed.
        (_, string opened) = await Get(client, Url("STORYLET_90"));
        Assert.Equal("Trait", Selected(opened, "cast[0].when[0].kind"));
        Assert.Equal("trait_b", Selected(opened, "cast[0].when[0].name"));
        Assert.Equal("true", Value(opened, "options[0].saved"));
        Assert.Equal("hold_service", Value(opened, "options[0].id"));
        Assert.Contains("type=\"hidden\" name=\"options[0].id\"", opened);
    }

    [Fact]
    public async Task The_blank_form_suggests_the_next_id_and_starts_with_a_subject_and_two_options()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Get(app.NewClient(), "/dev/content/storylets/new");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(NewIds.Next(app.Editor.Read(), ContentKind.Storylet), Value(body, "id"));
        Assert.Equal("subject", Value(body, "cast[0].name"));
        Assert.Contains("id=\"tab-option-1\"", body);
        Assert.DoesNotContain("id=\"tab-option-2\"", body);
        // A label fills its id box as it is typed (editor.js).
        Assert.Contains("data-id-to=\"f-options-0-id\"", body);
        Assert.Matches("<input id=\"f-options-0-id\"[^>]*data-auto=\"true\"", body);
    }

    [Fact]
    public async Task A_check_option_with_success_and_failure_effects_saves()
    {
        using TestApp app = NewApp();
        string check = Merge(NewStorylet, "options[0].id=talk&options[0].settled=check&options[0].skill=SKILL_B&options[0].difficulty=3"
            + "&options[0].success.effects[0].what=trait&options[0].success.effects[0].trait=trait_b&options[0].success.effects[0].mode=set&options[0].success.effects[0].amount=0"
            + "&options[0].success.effects[1].what=morale&options[0].success.effects[1].amount=2&options[0].success.effects[1].on=handler"
            + "&options[0].success.text=%23handler%23 STORYLET_90_SUCCESS"
            + "&options[0].failure.effects[0].what=trait&options[0].failure.effects[0].trait=trait_b&options[0].failure.effects[0].mode=set&options[0].failure.effects[0].amount=0"
            + "&options[0].failure.effects[1].what=loyalty&options[0].failure.effects[1].amount=-1&options[0].failure.effects[1].on=subject"
            + "&options[0].failure.text=%23handler%23 STORYLET_90_FAILURE&options[0].chronicleWeight=3");

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/storylets", check);

        Assert.True(status == HttpStatusCode.OK, Regex.Match(body, "class=\"alert\".*?</div>", RegexOptions.Singleline).Value + string.Join(" | ", Regex.Matches(body, "<p class=\"error\"[^>]*>([^<]*)</p>").Select(m => m.Groups[1].Value)));
        Assert.Contains(
            "    - id: talk\n      label: Hold a service\n      check: { skill: SKILL_B, difficulty: 3 }\n"
            + "      success:\n        effects: [{ trait: trait_b, set: 0 }, { morale: 2, on: handler }]\n        text: \"#handler# STORYLET_90_SUCCESS\"\n"
            + "      failure:\n        effects: [{ trait: trait_b, set: 0 }, { loyalty: -1, on: subject }]\n        text: \"#handler# STORYLET_90_FAILURE\"\n"
            + "      chronicle: { weight: 3 }\n",
            Disk(app, "storylets/base.yaml"),
            StringComparison.Ordinal);
        Assert.Contains(app.Live.Pack.Storylets, s => s.Id == "STORYLET_90" && s.Options[0].Check is { Skill: "SKILL_B", Difficulty: 3 });
    }

    // Design 4.3: handler names the one sent, so it is a slot only in a check's success and failure.
    [Fact]
    public async Task The_handler_is_refused_outside_a_checks_outcomes()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/storylets", Merge(NewStorylet, "options[0].effects[1].on=handler"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("<p class=\"error\" id=\"f-options-0-effects-1-on-error\">Choose the whole crew or a slot.</p>", body);
        Assert.Equal(before, Everything(app));
    }

    // --- Options keep their ids ---

    [Fact]
    public async Task Moving_and_removing_options_keeps_their_ids_and_what_is_in_them()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        (_, string body) = await Get(client, Url("STORYLET_04"));

        // option_a (a check) moves below option_b (with requires); then option_a is removed.
        (HttpStatusCode moved, string after) = await Post(client, Url("STORYLET_04") + "/rows", FormFields(body) + "&row=down:options:0");
        Assert.Equal(HttpStatusCode.OK, moved);
        Assert.Equal(["option_b", "option_a", "option_c"], [Value(after, "options[0].id")!, Value(after, "options[1].id")!, Value(after, "options[2].id")!]);
        Assert.Equal("check", Selected(after, "options[1].settled"));
        Assert.Equal("Stat", Selected(after, "options[0].requires[0].kind"));
        Assert.Equal("1", Value(after, "tab"));

        (_, string removed) = await Post(client, Url("STORYLET_04") + "/rows", FormFields(after) + "&row=remove:options:1");
        Assert.Equal(["option_b", "option_c"], [Value(removed, "options[0].id")!, Value(removed, "options[1].id")!]);
        Assert.Null(Value(removed, "options[2].id"));

        (HttpStatusCode saved, _) = await Post(client, Url("STORYLET_04"), FormFields(removed));
        Assert.Equal(HttpStatusCode.OK, saved);
        StoryletModel stored = ContentEdits.Get<StoryletModel>(app.Editor.Read(), ContentKind.Storylet, "STORYLET_04")!;
        Assert.Equal(["option_b", "option_c"], stored.Options.Select(o => o.Id));
        Assert.Equal("subject.loyalty >= 60", stored.Options[0].Requires);
        Assert.Equal([new StoryletEffectModel("trait", 0, "trait_h", true, null, null, "target"), new StoryletEffectModel("loyalty", -1, On: "subject")], stored.Options[0].Effects);
    }

    // Review focus 5: saves, events, and the chronicle name an option by its id.
    [Fact]
    public async Task A_saved_options_id_cannot_be_changed()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);
        (_, string body) = await Get(client, Url("STORYLET_02"));
        Assert.Equal("true", Value(body, "options[0].saved"));

        (HttpStatusCode status, string refused) = await Post(client, Url("STORYLET_02"), Merge(FormFields(body), "options[0].id=option_z"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("An option&#x27;s id is fixed once it is saved.", refused);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_new_options_id_is_made_from_its_label_and_passes_over_one_already_given()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/storylets", Merge(NewStorylet, "options[1].id=&options[1].label=The hold, a service"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["hold_service", "hold_service_2"], ContentEdits.Get<StoryletModel>(app.Editor.Read(), ContentKind.Storylet, "STORYLET_90")!.Options.Select(o => o.Id));
    }

    // --- The rows ---

    [Fact]
    public async Task Choosing_a_trait_with_named_levels_in_an_effect_row_makes_it_a_set_by_name()
    {
        using TestApp app = NewApp();
        string posted = Merge(NewStorylet, "options[1].effects[0].trait=trait_e");

        (_, string body) = await Post(app.NewClient(), "/dev/content/storylets/rows?focus=f-options-1-effects-0-trait", posted);

        Assert.Equal("set", Selected(body, "options[1].effects[0].mode"));
        Assert.Equal("level_b", Selected(body, "options[1].effects[0].amount"));
        Assert.Contains("data-init=\"el.focus()", Regex.Match(body, "<select id=\"f-options-1-effects-0-trait\"[^>]*>").Value);
    }

    [Fact]
    public async Task Adding_a_slot_and_an_effect_and_switching_how_an_option_is_settled_keep_the_rest()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string slot) = await Post(client, "/dev/content/storylets/rows", NewStorylet + "&row=add:cast");
        Assert.NotNull(Value(slot, "cast[1].name"));
        Assert.Equal("trait_b", Selected(slot, "cast[0].when[0].name"));

        (_, string effect) = await Post(client, "/dev/content/storylets/rows", NewStorylet + "&row=" + Uri.EscapeDataString("add:options[0].effects"));
        Assert.Equal("", Selected(effect, "options[0].effects[2].what"));
        Assert.Equal("subject", Selected(effect, "options[0].effects[1].on"));

        // To a check and back: the effects and text wait in hidden fields, and a difficulty is started.
        (_, string toCheck) = await Post(client, "/dev/content/storylets/rows?focus=f-options-0-settled", Merge(NewStorylet, "options[0].settled=check"));
        Assert.Equal("1", Value(toCheck, "options[0].difficulty"));
        Assert.Contains("type=\"hidden\" name=\"options[0].effects[1].on\" value=\"subject\"", toCheck);
        (_, string back) = await Post(client, "/dev/content/storylets/rows?focus=f-options-0-settled", Merge(FormFields(toCheck), "options[0].settled=effects"));
        Assert.Equal("subject", Selected(back, "options[0].effects[1].on"));
    }

    // --- The loader's refusals go to their fields (Step 5) ---

    // With the pack broken by this storylet's own slot, nothing can check an expression as typed; the save is the loader's, and its
    // refusal of the slot's expression is shown at its character, beside the widget's box.
    [Fact]
    public async Task A_loader_refusal_of_a_slots_expression_is_shown_beside_its_box()
    {
        using TestApp app = NewApp(root =>
        {
            string file = Path.Combine(root, "storylets", "base.yaml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("subject: \"subject.trait.trait_b >= 1 && subject.trait.trait_c == 0\"", "subject: \"subject.trait.nosuch >= 1\"", StringComparison.Ordinal));
        });
        HttpClient client = app.NewClient();
        string before = Everything(app);
        (_, string body) = await Get(client, Url("STORYLET_02"));
        Assert.Equal("text", Value(body, "cast[0].when.mode"));

        (HttpStatusCode status, string refused) = await Post(client, Url("STORYLET_02"), Merge(FormFields(body), "cast[0].when.text=" + Uri.EscapeDataString("subject.trait.trait_b >= 1 && subject.trait.unknown_one >= 1")));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        string line = Regex.Match(refused, "<div class=\"expr__check\" id=\"f-cast-0-when-check\".*?</div>", RegexOptions.Singleline).Value;
        Assert.Matches("<p class=\"error\" id=\"f-cast-0-when-check-error\">[^<]*unknown_one[^<]*\\(character [0-9]+\\)</p>", WebUtility.HtmlDecode(line));
        Assert.DoesNotContain("cast.subject, at character", WebUtility.HtmlDecode(refused));
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public void A_refusal_of_rows_goes_to_the_clause_that_holds_its_character()
    {
        var scope = new ExpressionScope("requires", "Requires", ["subject"]);
        var values = new FormValues(Fields(
            "requires.mode=rows&requires[0].kind=Stat&requires[0].who=subject&requires[0].name=morale&requires[0].op=>%3D&requires[0].value=1"
            + "&requires[1].kind=Presence&requires[1].who=subject&requires[1].name=aboard"));

        // "subject.morale >= 1 && subject.aboard": the second clause starts at character 22.
        Assert.Equal(("requires[0].kind", "X"), Web.Forms.ExpressionField.Place(values, scope, ExpressionChoices.None, 5, "X"));
        Assert.Equal(("requires[1].kind", "X"), Web.Forms.ExpressionField.Place(values, scope, ExpressionChoices.None, 23, "X"));
        Assert.Equal(("requires.text", "X (character 4)"), Web.Forms.ExpressionField.Place(values.With("requires.mode", "text"), scope, ExpressionChoices.None, 3, "X"));
    }

    [Fact]
    public void The_loaders_other_refusals_of_a_storylet_go_beside_their_fields()
    {
        var form = new StoryletKind();
        FormValues values = new(Fields(NewStorylet));
        FormChoices choices = FormChoicesReader.Read(ContentFileSet.ReadDirectory(Path.Combine(TestApp.RepositoryRoot(), "tests", "fixtures", "content")));

        (string Field, string Message)? Placed(string message) => form.Place("STORYLET_90", new ContentError("storylets/base.yaml", message), values, choices);

        Assert.Equal(("options[1].label", "Label must be 1 to 40 characters on one line, with no spaces at either end."), Placed("STORYLET_90: Option 'option_b': label must be 1 to 40 characters on one line, with no spaces at either end."));
        Assert.Equal(("script", "Cannot be used: no such symbol."), Placed("STORYLET_90: text cannot be used: no such symbol."));
        Assert.Equal(("options[1].text", "Cannot be read: bad."), Placed("STORYLET_90: option_b.text cannot be read: bad."));
        Assert.Equal(("stakes", "A storylet's stakes must be 1 to 5."), Placed("STORYLET_90: A storylet's stakes must be 1 to 5."));
        Assert.Equal("cast[0].when[0].kind", Placed("STORYLET_90: cast.subject, at character 3: nope")?.Field);
        Assert.Null(Placed("STORYLET_02: A storylet's stakes must be 1 to 5."));
        Assert.Null(Placed("STORYLET_90: Option 'option_b' leaves every expression true, so this storylet would come back every cycle."));
    }

    // --- Rows that carry what is nested in them ---

    [Fact]
    public void A_carrying_list_moves_and_drops_the_fields_nested_in_its_rows()
    {
        var set = new RowSet("options", ["id"], Qualified: true, Carry: true);
        var values = new FormValues(Fields("options[0].id=a&options[0].requires[0].kind=Stat&options[0].requires.mode=rows&options[1].id=b&options[1].effects[0].what=morale&row=down:options:0"));

        FormRows.Applied moved = FormRows.Apply(values, [set]);

        Assert.Equal("b", moved.Values.Get("options[0].id"));
        Assert.Equal("morale", moved.Values.Get("options[0].effects[0].what"));
        Assert.Equal("Stat", moved.Values.Get("options[1].requires[0].kind"));
        Assert.Equal("rows", moved.Values.Get("options[1].requires.mode"));
        Assert.False(moved.Values.Has("options[1].effects[0].what"));

        FormRows.Applied removed = FormRows.Apply(moved.Values.With("row", "remove:options:0"), [set]);
        Assert.Equal("a", removed.Values.Get("options[0].id"));
        Assert.False(removed.Values.Has("options[0].effects[0].what"));
        Assert.Equal("Stat", removed.Values.Get("options[0].requires[0].kind"));
    }

    // The fixture is the editor's to rewrite; the repository's content is not touched.
    [Fact]
    public async Task The_repositorys_own_content_is_byte_identical_after_storylet_saves()
    {
        using TestApp app = NewApp();
        await Post(app.NewClient(), "/dev/content/storylets", NewStorylet);

        guard.Verify();
    }
}

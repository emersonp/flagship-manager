using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core.Content;

namespace Web.Tests;

// The first editor forms (skills, gear, starting crew, assignments), through the real app on a scratch copy of the content.
public sealed class ContentFormsTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    internal static TestApp NewApp(Action<string>? prepare = null)
    {
        var app = new TestApp(developerMode: true, scratch: true, prepare: prepare);
        // Starts the host, which makes the scratch copy.
        _ = app.Host;
        return app;
    }

    internal static async Task<(HttpStatusCode Status, string Body)> Get(HttpClient client, string url)
    {
        HttpResponseMessage response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static List<KeyValuePair<string, string>> Fields(string text)
    {
        return text.Length == 0
            ? []
            : text.Split('&').Select(p => p.Split('=', 2)).Select(p => new KeyValuePair<string, string>(Uri.UnescapeDataString(p[0]), Uri.UnescapeDataString(p.Length > 1 ? p[1] : ""))).ToList();
    }

    internal static async Task<(HttpStatusCode Status, string Body)> Post(HttpClient client, string url, string fields, bool datastar = false, Action<HttpRequestHeaders>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(Fields(fields)) };
        if (datastar)
        {
            request.Headers.Add("Datastar-Request", "true");
        }

        headers?.Invoke(request.Headers);
        HttpResponseMessage response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static string Disk(TestApp app, string file)
    {
        return File.ReadAllText(Path.Combine(app.ContentPath, file));
    }

    internal static string Everything(TestApp app)
    {
        return string.Join("\n", Directory.EnumerateFiles(app.ContentPath, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Select(f => f + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));
    }

    // The value attribute of the first input with this name, decoded.
    internal static string? Value(string body, string name)
    {
        Match tag = Regex.Match(body, $"<input\\b[^>]*\\bname=\"{Regex.Escape(name)}\"[^>]*>");
        Match value = Regex.Match(tag.Value, "\\bvalue=\"([^\"]*)\"");
        return tag.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }

    internal static string? Selected(string body, string name)
    {
        Match select = Regex.Match(body, $"<select\\b[^>]*\\bname=\"{Regex.Escape(name)}\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
        Match option = Regex.Match(select.Value, "<option\\b[^>]*\\bvalue=\"([^\"]*)\"[^>]*\\bselected\\b");
        return select.Success ? option.Groups[1].Value : null;
    }

    internal static List<string> Checked(string body, string name)
    {
        return Regex.Matches(body, $"<input\\b[^>]*\\bname=\"{Regex.Escape(name)}\"[^>]*>").Select(m => m.Value).Where(t => Regex.IsMatch(t, "\\bchecked\\b")).Select(t => Regex.Match(t, "\\bvalue=\"([^\"]*)\"").Groups[1].Value).ToList();
    }

    private static string Crew(string overrides = "", string id = "")
    {
        var fields = Fields((id.Length > 0 ? $"id={id}&" : "") + "name=NAME_PLACEHOLDER&seniority=5&caution=2&discipline=2&skills.SKILL_A=6&skills.SKILL_B=5&skills.SKILL_C=4&skills.SKILL_D=3");
        foreach (KeyValuePair<string, string> over in Fields(overrides))
        {
            fields.RemoveAll(f => f.Key == over.Key);
            fields.Add(over);
        }

        return string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
    }

    private const string Assignment = "name=NAME_PLACEHOLDER&loadoutLimit=2&offer[0].count=1&offer[0].weight=6&offer[1].count=2&offer[1].weight=3";

    // --- The forms' values ---

    [Fact]
    public async Task The_skill_form_shows_the_id_fixed_and_the_name_to_edit()
    {
        using TestApp app = NewApp();
        await app.Editor.Save(files => ContentEdits.Upsert(files, ContentKind.Skill, ContentEdits.Get<SkillModel>(files, ContentKind.Skill, "SKILL_A")! with { Name = "NAME_PLACEHOLDER_A" }));

        (HttpStatusCode status, string body) = await Get(app.NewClient(), "/dev/content/skills/SKILL_A");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("NAME_PLACEHOLDER_A", Value(body, "name"));
        Assert.Contains(">SKILL_A<", body);
        Assert.Null(Value(body, "id"));
        Assert.Contains("href=\"/dev/content/skills\"", body);
    }

    [Fact]
    public async Task The_gear_form_shows_the_tag_as_text_with_the_tags_in_use_to_pick_from()
    {
        using TestApp app = NewApp();
        await Post(app.NewClient(), "/dev/content/gear", "id=GEAR_05&tag=extra_tag");

        string body = (await Get(app.NewClient(), "/dev/content/gear/GEAR_03")).Body;

        Assert.Equal("comms", Value(body, "tag"));
        Assert.Matches("<input\\b[^>]*list=\"f-tag-list\"", body);
        foreach (string tag in new[] { "comms", "medical", "scanner", "tools", "extra_tag" })
        {
            Assert.Contains($"<option value=\"{tag}\"", body);
        }

        Assert.Contains("A tag matters once an encounter asks for it.", body);
    }

    [Fact]
    public async Task A_gear_item_can_take_a_tag_no_one_uses_yet()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/gear/GEAR_01", "name=NAME_PLACEHOLDER&tag=%20brand_new%20");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("brand_new", app.Live.Pack.Gear.Single(g => g.Id == "GEAR_01").Tag);
    }

    [Fact]
    public async Task The_crew_form_shows_the_numbers_a_level_for_every_skill_and_says_what_it_is_for()
    {
        using TestApp app = NewApp();
        await app.Editor.Save(files => ContentEdits.Upsert(files, ContentKind.Skill, new SkillModel("SKILL_E", null, [])));

        string body = (await Get(app.NewClient(), "/dev/content/crew/CREW_01")).Body;

        Assert.Equal("5", Value(body, "seniority"));
        Assert.Equal("2", Value(body, "caution"));
        Assert.Equal("2", Value(body, "discipline"));
        Assert.Equal("6", Value(body, "skills.SKILL_A"));
        Assert.Equal("3", Value(body, "skills.SKILL_D"));
        Assert.Equal("", Value(body, "skills.SKILL_E"));
        Assert.Contains("Crew will be generated from sources later, so this form only names the starting eight and sets their numbers.", body);
        Assert.Contains("Blank means the crew member does not have the skill and cannot train it. 0 is untrained.", body);
    }

    // A skill new to the pack is one no starting crew member has yet: blank on every crew form, not 0.
    [Fact]
    public async Task A_skill_added_to_the_pack_shows_blank_on_every_crew_form()
    {
        using TestApp app = NewApp();
        await app.Editor.Save(files => ContentEdits.Upsert(files, ContentKind.Skill, new SkillModel("SKILL_E", null, [])));
        HttpClient client = app.NewClient();

        foreach (string id in app.Live.Pack.Crew.Select(c => c.Id).Append("new"))
        {
            Assert.Equal("", Value((await Get(client, $"/dev/content/crew/{id}")).Body, "skills.SKILL_E"));
        }
    }

    [Fact]
    public async Task The_assignment_form_shows_the_offer_as_rows_the_limit_and_the_objective_tags_as_checkboxes()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        string body = (await Get(client, "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1")).Body;

        Assert.Equal("1", Value(body, "offer[0].count"));
        Assert.Equal("6", Value(body, "offer[0].weight"));
        Assert.Equal("2", Value(body, "offer[1].count"));
        Assert.Equal("3", Value(body, "offer[1].weight"));
        Assert.Null(Value(body, "offer[2].count"));
        Assert.Equal("2", Value(body, "loadoutLimit"));
        Assert.Empty(Checked(body, "tags"));
        Assert.Contains("value=\"repair\"", body);
        Assert.Contains("value=\"science\"", body);

        // One objective has the tag, so the offer can only reach one.
        SaveResult saved = await app.Editor.Save(files => ContentEdits.Upsert(files, ContentKind.Assignment, ContentEdits.Get<AssignmentModel>(files, ContentKind.Assignment, "ASSIGNMENT_TEMPLATE_1")! with { ObjectiveTags = ["science"], Offer = new Dictionary<int, int> { [1] = 6 } }));
        Assert.True(saved.Saved);
        string ticked = (await Get(client, "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1")).Body;
        Assert.Equal(["science"], Checked(ticked, "tags"));
        Assert.Null(Value(ticked, "offer[1].count"));
    }

    [Fact]
    public async Task A_new_form_asks_for_the_id_and_shows_the_rule_in_words()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        foreach (string kind in new[] { "skills", "gear", "crew", "assignments" })
        {
            (HttpStatusCode status, string body) = await Get(client, $"/dev/content/{kind}/new");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("", Value(body, "id"));
            Assert.Contains(Ids.ItemRule, body);
            Assert.DoesNotContain("/delete", body);
        }
    }

    // --- Saving ---

    [Fact]
    public async Task Saving_a_skill_writes_the_name_and_the_game_shows_it()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/skills/SKILL_B", "name=NAME_PLACEHOLDER_B");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Saved.", body);
        Assert.Contains("name: NAME_PLACEHOLDER_B", Disk(app, "skills/base.yaml"));
        Assert.Equal("NAME_PLACEHOLDER_B", app.Live.Names.Skill("SKILL_B"));
        Assert.Equal("NAME_PLACEHOLDER_B", Value((await Get(client, "/dev/content/skills/SKILL_B")).Body, "name"));
    }

    [Fact]
    public async Task Saving_gear_changes_the_name_and_the_tag()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/gear/GEAR_01", "name=NAME_PLACEHOLDER&tag=tools");

        Assert.Equal(HttpStatusCode.OK, status);
        GearDef gear = app.Live.Pack.Gear.Single(g => g.Id == "GEAR_01");
        Assert.Equal("tools", gear.Tag);
        Assert.Equal("NAME_PLACEHOLDER", gear.Name);
        Assert.Contains("tag: tools", Disk(app, "gear/base.yaml"));
    }

    [Fact]
    public async Task Saving_crew_changes_the_name_a_number_and_a_level_and_a_new_game_starts_with_them()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/crew/CREW_01", Crew("name=NAME_PLACEHOLDER_ONE&seniority=7&skills.SKILL_A=8"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Saved.", body);
        CrewDef crew = app.Live.Pack.Crew.Single(c => c.Id == "CREW_01");
        Assert.Equal("NAME_PLACEHOLDER_ONE", crew.Name);
        Assert.Equal(7, crew.Seniority);
        Assert.Equal(8, crew.Skills["SKILL_A"]);
        Assert.Equal(5, crew.Skills["SKILL_B"]);
        Assert.Contains("seniority: 7", Disk(app, "crew/starting.yaml"));
    }

    // 0 is untrained but trainable, so the key is written; a member can only train a skill they have a key for.
    [Fact]
    public async Task A_skill_at_zero_is_written_and_a_new_legacy_starts_with_it()
    {
        using TestApp app = NewApp();
        await app.Editor.Save(files => ContentEdits.Upsert(files, ContentKind.Skill, new SkillModel("SKILL_E", null, [])));

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/crew/CREW_02", Crew("skills.SKILL_E=0"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("SKILL_E: 0", Disk(app, "crew/starting.yaml"));
        Assert.Equal(0, app.Live.Pack.Crew.Single(c => c.Id == "CREW_02").Skills["SKILL_E"]);
        Assert.Equal("0", Value((await Get(app.NewClient(), "/dev/content/crew/CREW_02")).Body, "skills.SKILL_E"));
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        LoadedLegacy legacy = (await app.Sessions.Read(app.Live, id))!;
        Sim.Crew.CrewMember member = legacy.State.Module<Sim.Crew.CrewState>(Sim.Crew.CrewModule.Name).Members.Single(m => m.Id == "CREW_02");
        Assert.Equal(0, member.Skills["SKILL_E"].Level);
    }

    // Blank means the member does not have the skill: no key is written, and the form reads back blank.
    [Fact]
    public async Task A_blank_skill_writes_no_key_and_reads_back_blank()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/crew/CREW_01", Crew("skills.SKILL_D=%20"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(app.Live.Pack.Crew.Single(c => c.Id == "CREW_01").Skills.ContainsKey("SKILL_D"));
        Assert.Contains("skills: { SKILL_A: 6, SKILL_B: 5, SKILL_C: 4 }", Disk(app, "crew/starting.yaml"));
        Assert.Equal("", Value((await Get(app.NewClient(), "/dev/content/crew/CREW_01")).Body, "skills.SKILL_D"));
    }

    // A skill some starting crew have, added to the scratch copy before the app starts.
    private static void AddSkillE(string root)
    {
        File.AppendAllText(Path.Combine(root, "skills", "base.yaml"), "- id: SKILL_E\n");
        string crew = Path.Combine(root, "crew", "starting.yaml");
        string text = File.ReadAllText(crew);
        int at = 0;
        foreach (string id in new[] { "CREW_01", "CREW_02", "CREW_03" })
        {
            at = text.IndexOf($"- id: {id}\n", at, StringComparison.Ordinal);
            int end = text.IndexOf(" }", at, StringComparison.Ordinal);
            text = text[..end] + ", SKILL_E: 1" + text[end..];
        }

        File.WriteAllText(crew, text);
    }

    // The one save that changes two files: the skill goes from the crew who have it and from the skills file, and the pack loads.
    [Fact]
    public async Task Deleting_a_skill_crew_have_removes_it_from_them_and_from_the_skills_file()
    {
        using TestApp app = NewApp(AddSkillE);
        HttpClient client = app.NewClient();

        string form = (await Get(client, "/dev/content/skills/SKILL_E")).Body;
        (HttpStatusCode status, string list) = await Post(client, "/dev/content/skills/SKILL_E/delete", "");

        Assert.Contains("This also removes it from 3 starting crew.", form);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Deleted SKILL_E.", list);
        Assert.DoesNotContain("SKILL_E", Disk(app, "skills/base.yaml"));
        Assert.DoesNotContain("SKILL_E", Disk(app, "crew/starting.yaml"));
        Assert.NotNull(app.Editor.Read().Validate().Pack);
        Assert.All(app.Live.Pack.Crew, c => Assert.False(c.Skills.ContainsKey("SKILL_E")));
    }

    // Encounters still hold the delete back through the loader, and neither file is written.
    [Fact]
    public async Task Deleting_a_skill_an_encounter_uses_is_refused_and_writes_neither_file()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/skills/SKILL_A/delete", "");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("unknown skill &#x27;SKILL_A&#x27;", body);
        Assert.Equal(before, Everything(app));
    }

    // A skill no starting crew member has says nothing more in its confirmation.
    [Fact]
    public async Task A_skill_no_crew_have_has_no_extra_words_in_its_delete_confirmation()
    {
        using TestApp app = NewApp(root => File.AppendAllText(Path.Combine(root, "skills", "base.yaml"), "- id: SKILL_E\n"));

        string form = (await Get(app.NewClient(), "/dev/content/skills/SKILL_E")).Body;

        Assert.DoesNotContain("This also removes it", form);
    }

    [Fact]
    public async Task Saving_an_assignment_changes_the_name_the_limit_the_offer_and_the_tags()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "name=NAME_PLACEHOLDER&loadoutLimit=3&tags=science&offer[0].count=1&offer[0].weight=4");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Saved.", body);
        AssignmentDef assignment = app.Live.Pack.Assignments.Single();
        Assert.Equal(3, assignment.LoadoutLimit);
        Assert.Equal(["science"], assignment.ObjectiveTags);
        Assert.Equal(new Dictionary<int, int> { [1] = 4 }, assignment.Offer);
        Assert.Equal("NAME_PLACEHOLDER", assignment.Name);
    }

    [Fact]
    public async Task A_posted_id_on_an_existing_item_is_ignored_and_the_route_decides()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/skills/SKILL_A", "id=SKILL_Z&name=NAME_PLACEHOLDER");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("SKILL_Z", Disk(app, "skills/base.yaml"));
        Assert.Equal("NAME_PLACEHOLDER", app.Live.Names.Skill("SKILL_A"));
    }

    [Fact]
    public async Task Html_in_a_name_is_shown_as_text_in_the_field_and_the_heading()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string saved) = await Post(client, "/dev/content/skills/SKILL_A", "name=" + Uri.EscapeDataString("<b>bold</b> & \"quoted\""));
        string page = (await Get(client, "/dev/content/skills/SKILL_A")).Body;

        foreach (string body in new[] { saved, page })
        {
            Assert.DoesNotContain("<b>bold", body);
            Assert.Contains("&lt;b&gt;bold&lt;/b&gt;", body);
            Assert.Equal("<b>bold</b> & \"quoted\"", Value(body, "name"));
        }
    }

    // --- Creating ---

    [Fact]
    public async Task Creating_each_kind_writes_the_item_and_answers_with_its_form()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode skill, string skillBody) = await Post(client, "/dev/content/skills", "id=SKILL_E&name=NAME_PLACEHOLDER_E");
        (HttpStatusCode gear, _) = await Post(client, "/dev/content/gear", "id=GEAR_05&name=NAME_PLACEHOLDER&tag=medical");
        (HttpStatusCode crew, _) = await Post(client, "/dev/content/crew", Crew("skills.SKILL_E=1", "CREW_09"));
        (HttpStatusCode assignment, string assignmentBody) = await Post(client, "/dev/content/assignments", "id=ASSIGNMENT_2&name=NAME_PLACEHOLDER&loadoutLimit=1&offer[0].count=1&offer[0].weight=1");

        Assert.All(new[] { skill, gear, crew, assignment }, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Contains("Saved.", skillBody);
        Assert.Contains("action=\"/dev/content/skills/SKILL_E\"", skillBody);
        Assert.Contains("action=\"/dev/content/assignments/ASSIGNMENT_2\"", assignmentBody);
        Assert.Equal("NAME_PLACEHOLDER_E", app.Live.Names.Skill("SKILL_E"));
        Assert.Equal("medical", app.Live.Pack.Gear.Single(g => g.Id == "GEAR_05").Tag);
        Assert.Equal(1, app.Live.Pack.Crew.Single(c => c.Id == "CREW_09").Skills["SKILL_E"]);
        Assert.Equal(2, app.Live.Pack.Assignments.Count);
    }

    [Fact]
    public async Task A_datastar_create_sends_the_browser_to_the_new_items_page()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string stream) = await Post(app.NewClient(), "/dev/content/skills", "id=SKILL_E&name=NAME_PLACEHOLDER_E", datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("event: datastar-patch-elements", stream);
        Assert.Contains("data: selector body", stream);
        Assert.Contains("data: mode append", stream);
        Assert.Contains("window.location = \"/dev/content/skills/SKILL_E?saved=1\"", stream);
        Assert.DoesNotContain("<main", stream);
    }

    [Fact]
    public async Task The_new_items_page_shows_saved_only_when_asked_and_a_save_in_place_still_patches_the_form()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        string plain = (await Get(client, "/dev/content/skills/SKILL_A")).Body;
        string saved = (await Get(client, "/dev/content/skills/SKILL_A?saved=1")).Body;
        (_, string stream) = await Post(client, "/dev/content/skills/SKILL_A", "name=NAME_PLACEHOLDER", datastar: true);

        Assert.DoesNotContain("Saved.", plain);
        Assert.Contains("Saved.", saved);
        Assert.Contains("data: elements <main id=\"main\"", stream);
        Assert.Contains("Saved.", stream);
        Assert.DoesNotContain("<script", stream);
    }

    // --- Deleting ---

    [Fact]
    public async Task Deleting_an_unused_item_removes_it_and_answers_with_the_kinds_list()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        await Post(client, "/dev/content/skills", "id=SKILL_E");
        await Post(client, "/dev/content/assignments", "id=ASSIGNMENT_2&loadoutLimit=1&offer[0].count=1&offer[0].weight=1");

        (HttpStatusCode gear, string gearBody) = await Post(client, "/dev/content/gear/GEAR_04/delete", "");
        (HttpStatusCode skill, _) = await Post(client, "/dev/content/skills/SKILL_E/delete", "");
        (HttpStatusCode crew, _) = await Post(client, "/dev/content/crew/CREW_08/delete", "");
        (HttpStatusCode assignment, _) = await Post(client, "/dev/content/assignments/ASSIGNMENT_2/delete", "");

        Assert.All(new[] { gear, skill, crew, assignment }, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Contains("Deleted GEAR_04.", gearBody);
        Assert.Contains("href=\"/dev/content/gear/new\"", gearBody);
        Assert.DoesNotContain("GEAR_04", Disk(app, "gear/base.yaml"));
        Assert.DoesNotContain(app.Live.Pack.Skills, s => s.Id == "SKILL_E");
        Assert.DoesNotContain(app.Live.Pack.Crew, c => c.Id == "CREW_08");
        Assert.Single(app.Live.Pack.Assignments);
    }

    [Fact]
    public async Task A_datastar_delete_sends_the_browser_to_the_list_and_the_list_shows_the_notice_encoded()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string stream) = await Post(client, "/dev/content/gear/GEAR_04/delete", "", datastar: true);
        string list = (await Get(client, "/dev/content/gear?deleted=" + Uri.EscapeDataString("<b>X</b>"))).Body;

        Assert.Contains("window.location = \"/dev/content/gear?deleted=GEAR_04\"", stream);
        Assert.DoesNotContain("<main", stream);
        Assert.Contains("Deleted &lt;b&gt;X&lt;/b&gt;.", list);
        Assert.DoesNotContain("<b>X", list);
    }

    [Fact]
    public async Task Deleting_what_is_already_gone_answers_the_list_with_a_notice_and_no_error()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/gear/NOT_THERE/delete", "");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("NOT_THERE is not there, so nothing was deleted.", body);
        Assert.Contains("href=\"/dev/content/gear/new\"", body);
        Assert.Equal(before, Everything(app));
    }

    // --- Races under the lock ---

    [Fact]
    public async Task A_create_whose_id_appears_between_the_check_and_the_save_is_refused_and_the_first_item_is_untouched()
    {
        using TestApp app = NewApp();
        app.Editor.AfterGate = () =>
        {
            app.Editor.AfterGate = null;
            ContentFileSet files = app.Editor.Read();
            ContentEdits.Write(app.ContentPath, files, ContentEdits.Upsert(files, ContentKind.Skill, new SkillModel("SKILL_E", "NAME_PLACEHOLDER_FIRST", [])));
        };

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/skills", "id=SKILL_E&name=NAME_PLACEHOLDER_SECOND");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("That id is already used by a skill.", body);
        Assert.Contains("NAME_PLACEHOLDER_FIRST", Disk(app, "skills/base.yaml"));
        Assert.DoesNotContain("NAME_PLACEHOLDER_SECOND", Disk(app, "skills/base.yaml"));
    }

    [Fact]
    public async Task A_save_of_an_item_deleted_meanwhile_is_refused()
    {
        using TestApp app = NewApp();
        app.Editor.AfterGate = () =>
        {
            app.Editor.AfterGate = null;
            ContentFileSet files = app.Editor.Read();
            ContentEdits.Write(app.ContentPath, files, ContentEdits.Delete(files, ContentKind.Gear, "GEAR_04"));
        };

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/gear/GEAR_04", "name=NAME_PLACEHOLDER&tag=medical");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("This item no longer exists.", body);
        Assert.DoesNotContain("GEAR_04", Disk(app, "gear/base.yaml"));
    }

    [Fact]
    public async Task A_delete_the_loader_refuses_shows_its_reasons_and_deletes_nothing()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);

        (HttpStatusCode skill, string skillBody) = await Post(client, "/dev/content/skills/SKILL_A/delete", "");
        (HttpStatusCode assignment, string assignmentBody) = await Post(client, "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/delete", "");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, skill);
        Assert.Contains("unknown skill 'SKILL_A'", WebUtility.HtmlDecode(skillBody));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, assignment);
        Assert.Contains("The pack has no assignment.", assignmentBody);
        Assert.Equal(before, Everything(app));
        Assert.Contains(app.Live.Pack.Skills, s => s.Id == "SKILL_A");
    }

    [Fact]
    public async Task Deleting_the_last_crew_member_is_refused()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        for (int n = 2; n <= 8; n++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Post(client, $"/dev/content/crew/CREW_0{n}/delete", "")).Status);
        }

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/crew/CREW_01/delete", "");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("The pack has no crew.", body);
        Assert.Contains(app.Live.Pack.Crew, c => c.Id == "CREW_01");
    }

    // --- Field checks ---

    public static TheoryData<string, string, string> FieldCases => new()
    {
        { "/dev/content/skills", "id=skill_x", Ids.ItemRule },
        { "/dev/content/skills", "id=", "Enter an id." },
        { "/dev/content/skills", "id=SKILL_A", "That id is already used by a skill." },
        { "/dev/content/skills", "id=CREW_01", "That id is already used by a crew member." },
        { "/dev/content/skills", "id=ENCOUNTER_01", "That id is already used by an encounter." },
        { "/dev/content/skills", "id=OBJECTIVE_TEMPLATE_1", "That id is already used by an objective." },
        { "/dev/content/skills", "id=ASSIGNMENT_TEMPLATE_1", "That id is already used by an assignment." },
        { "/dev/content/skills", "id=GEAR_01", "That id is already used by a gear item." },
        { "/dev/content/skills", "id=SKILL_E&name=" + "x82", "A name is 1 to 80 characters with no line break." },
        { "/dev/content/skills/SKILL_A", "name=" + "x82", "A name is 1 to 80 characters with no line break." },
        { "/dev/content/gear", "id=GEAR_05&tag=", "Enter a tag." },
        { "/dev/content/gear", "id=GEAR_05&tag=%20%20", "Enter a tag." },
        { "/dev/content/gear", "id=GEAR_05&tag=a%0Ab", "A tag is one line." },
        { "/dev/content/gear", "id=GEAR_05&tag=Scan-ner", Ids.InnerRule },
        { "/dev/content/crew/CREW_01", "seniority=", "Enter a whole number." },
        { "/dev/content/crew/CREW_01", "seniority=abc", "Must be a whole number." },
        { "/dev/content/crew/CREW_01", "seniority=1.5", "Must be a whole number." },
        { "/dev/content/crew/CREW_01", "seniority=0", "Must be 1 or more." },
        { "/dev/content/crew/CREW_01", "caution=3", "Must be -2 to 2." },
        { "/dev/content/crew/CREW_01", "discipline=-3", "Must be -2 to 2." },
        { "/dev/content/crew/CREW_01", "skills.SKILL_A=11", "Must be 0 to 10." },
        { "/dev/content/crew/CREW_01", "skills.SKILL_B=-1", "Must be 0 to 10." },
        { "/dev/content/crew/CREW_01", "skills.SKILL_C=x", "Must be a whole number." },
        { "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "loadoutLimit=-1", "Must be 0 or more." },
        { "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "loadoutLimit=x", "Must be a whole number." },
        { "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "offer[0].count=0", "Must be 1 or more." },
        { "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "offer[0].weight=0", "Must be 1 or more." },
        { "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "offer[0].count=", "Enter a whole number." },
        { "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "offer[1].count=1", "Each number of objectives can be offered once." },
        { "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "tags=nonsense", "Unknown tag." },
    };

    [Theory]
    [MemberData(nameof(FieldCases))]
    public async Task A_field_that_fails_its_check_is_explained_beside_it_and_nothing_is_written(string url, string fields, string words)
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);
        string posted = url.Contains("/crew/", StringComparison.Ordinal) ? Crew(fields) : url.Contains("/assignments/", StringComparison.Ordinal) ? Merge(Assignment, fields) : fields.Replace("x82", new string('x', 81), StringComparison.Ordinal);

        (HttpStatusCode status, string body) = await Post(client, url, posted);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains(words, body);
        Assert.Matches("<p class=\"error\" id=\"f-[A-Za-z0-9_-]+-error\">" + Regex.Escape(words), body);
        Assert.Matches("aria-describedby=\"[^\"]*f-[A-Za-z0-9_-]+-error", body);
        Assert.Contains("aria-invalid=\"true\"", body);
        Assert.Equal(before, Everything(app));
    }

    private static string Merge(string baseFields, string overrides)
    {
        List<KeyValuePair<string, string>> fields = Fields(baseFields);
        foreach (KeyValuePair<string, string> over in Fields(overrides))
        {
            fields.RemoveAll(f => f.Key == over.Key && f.Key != "tags");
            fields.Add(over);
        }

        return string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
    }

    [Fact]
    public async Task A_refused_form_keeps_what_was_typed()
    {
        using TestApp app = NewApp();

        (_, string body) = await Post(app.NewClient(), "/dev/content/crew/CREW_01", Crew("name=NAME_PLACEHOLDER_TYPED&seniority=abc&skills.SKILL_A=9"));

        Assert.Equal("abc", Value(body, "seniority"));
        Assert.Equal("NAME_PLACEHOLDER_TYPED", Value(body, "name"));
        Assert.Equal("9", Value(body, "skills.SKILL_A"));
    }

    [Fact]
    public async Task A_datastar_field_error_patches_the_form_with_the_words_and_writes_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string stream) = await Post(app.NewClient(), "/dev/content/crew/CREW_01", Crew("seniority=0"), datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("event: datastar-patch-elements", stream);
        Assert.Contains("Must be 1 or more.", stream);
        Assert.Equal(before, Everything(app));
    }

    // --- Loader refusals ---

    [Fact]
    public async Task A_change_only_the_loader_refuses_is_shown_in_the_alert_in_its_own_words_and_nothing_is_written()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode tooMany, string tooManyBody) = await Post(app.NewClient(), "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "name=NAME_PLACEHOLDER&loadoutLimit=2&offer[0].count=3&offer[0].weight=1");
        (HttpStatusCode none, string noneBody) = await Post(app.NewClient(), "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "name=NAME_PLACEHOLDER&loadoutLimit=2");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooMany);
        Assert.Matches("<div [^>]*id=\"form-alert\"[^>]*>.*offers up to 3 objectives", new string(tooManyBody.Where(c => c != '\n').ToArray()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, none);
        Assert.Contains("offer must list at least one count.", noneBody);
        Assert.Equal(before, Everything(app));
        Assert.Equal(2, app.Live.Pack.Assignments.Single().Offer.Count);
    }

    // --- Content the editor cannot keep ---

    private static void BreakGear(string root)
    {
        string gear = Path.Combine(root, "gear", "base.yaml");
        File.WriteAllText(gear, File.ReadAllText(gear).Replace("tag: scanner", "tag: scanner, typo: 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lossy_item_shows_a_warning_above_the_fields_with_the_loss()
    {
        using TestApp app = NewApp(BreakGear);

        string body = (await Get(app.NewClient(), "/dev/content/gear/GEAR_01")).Body;

        Assert.Contains("This item has content the editor cannot keep. Saving replaces it with what the form shows.", body);
        Assert.Contains("GEAR_01 has a key the editor cannot keep: typo.", body);
        Assert.True(body.IndexOf("cannot keep", StringComparison.Ordinal) < body.IndexOf("name=\"name\"", StringComparison.Ordinal), "The warning comes before the fields.");
    }

    [Fact]
    public async Task An_item_beside_a_lossy_one_warns_that_saving_is_refused_and_a_save_is_refused_naming_the_other_item()
    {
        using TestApp app = NewApp(BreakGear);
        HttpClient client = app.NewClient();
        string before = Everything(app);

        string page = (await Get(client, "/dev/content/gear/GEAR_02")).Body;
        (HttpStatusCode status, string body) = await Post(client, "/dev/content/gear/GEAR_02", "name=NAME_PLACEHOLDER&tag=tools");

        Assert.Contains("Another item in this file has content the editor cannot keep, so a save here is refused until it is fixed by hand.", page);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("GEAR_01 has a key the editor cannot keep: typo.", body);
        Assert.Equal(before, Everything(app));
    }

    // 1e part six: an inner comment belongs to no item, so every item's form says the file is refused and why, and a save leaves it as it was.
    [Fact]
    public async Task A_file_with_an_inner_comment_is_refused_by_its_forms_and_the_comment_kept()
    {
        using TestApp app = NewApp(root =>
        {
            string gear = Path.Combine(root, "gear", "base.yaml");
            File.WriteAllText(gear, File.ReadAllText(gear).Replace("- { id: GEAR_02, tag: tools }", "- { id: GEAR_02, tag: tools }  # kept for the vault", StringComparison.Ordinal));
        });
        HttpClient client = app.NewClient();
        string before = Everything(app);

        string page = (await Get(client, "/dev/content/gear/GEAR_02")).Body;
        (HttpStatusCode status, string body) = await Post(client, "/dev/content/gear/GEAR_01", "name=NAME_PLACEHOLDER&tag=scanner");

        Assert.Contains("This file has content the editor cannot keep, so a save here is refused until it is fixed by hand.", page);
        Assert.Contains("has a comment on line 3 that saving from a form would drop", page);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("has a comment on line 3 that saving from a form would drop", body);
        Assert.Equal(before, Everything(app));
        Assert.Contains("# kept for the vault", Disk(app, "gear/base.yaml"));
    }

    [Fact]
    public async Task Saving_the_lossy_item_itself_replaces_it_and_the_file_is_clean()
    {
        using TestApp app = NewApp(BreakGear);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/gear/GEAR_01", "name=NAME_PLACEHOLDER&tag=scanner");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Saved.", body);
        Assert.DoesNotContain("typo", Disk(app, "gear/base.yaml"));
        Assert.NotNull(app.Host.Current);
    }

    // --- Unknown ids and kinds ---

    [Theory]
    [InlineData("skills")]
    [InlineData("gear")]
    [InlineData("crew")]
    [InlineData("assignments")]
    public async Task An_unknown_id_is_404_to_open_and_save(string kind)
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, $"/dev/content/{kind}/NOT_THERE")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, $"/dev/content/{kind}/NOT_THERE", "name=NAME_PLACEHOLDER")).Status);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task An_id_that_is_in_another_kind_is_404()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/content/skills/CREW_01")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/content/objectives/ENCOUNTER_01")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/content/encounters/OBJECTIVE_TEMPLATE_1")).Status);
    }

    [Fact]
    public async Task With_developer_mode_off_the_form_routes_do_not_exist()
    {
        using var app = new TestApp();
        HttpClient client = app.NewClient();

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/content/skills/SKILL_A")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/dev/content/skills", "id=SKILL_E")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/dev/content/skills/SKILL_A/delete", "")).Status);
    }

    // --- Offer rows ---

    [Fact]
    public async Task Adding_a_row_re_renders_the_form_from_the_posted_fields_and_saves_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", Merge(Assignment, "name=TYPED_NAME&offer[1].weight=9&row=add"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("TYPED_NAME", Value(body, "name"));
        Assert.Equal("9", Value(body, "offer[1].weight"));
        Assert.Equal("", Value(body, "offer[2].count"));
        Assert.Equal("", Value(body, "offer[2].weight"));
        Assert.Null(Value(body, "offer[3].count"));
        Assert.DoesNotContain("Saved.", body);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task Removing_a_row_drops_that_row_and_the_others_keep_their_order()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (_, string body) = await Post(app.NewClient(), "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", Merge(Assignment, "row=remove:0"));

        Assert.Equal("2", Value(body, "offer[0].count"));
        Assert.Equal("3", Value(body, "offer[0].weight"));
        Assert.Null(Value(body, "offer[1].count"));
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_new_assignments_rows_use_the_new_route_and_a_bad_row_number_changes_nothing()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode status, string body) = await Post(client, "/dev/content/assignments/rows", "id=ASSIGNMENT_2&loadoutLimit=1&offer[0].count=1&offer[0].weight=1&row=add");
        (_, string bad) = await Post(client, "/dev/content/assignments/rows", "id=ASSIGNMENT_2&loadoutLimit=1&offer[0].count=1&offer[0].weight=1&row=remove:7");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("ASSIGNMENT_2", Value(body, "id"));
        Assert.Equal("", Value(body, "offer[1].count"));
        Assert.Null(Value(bad, "offer[1].count"));
        Assert.Equal("1", Value(bad, "offer[0].count"));
    }

    // A row number that is not a whole number of 0 or more is a bad request, not an action that quietly does nothing.
    [Theory]
    [InlineData("/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", "row=remove:abc")]
    [InlineData("/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", "row=remove:-1")]
    [InlineData("/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", "row=remove:")]
    [InlineData("/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", "row=up:1.5")]
    [InlineData("/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", "row=down:%200")]
    [InlineData("/dev/content/assignments/rows", "row=remove:x")]
    [InlineData("/dev/content/encounters/ENCOUNTER_01/rows", "row=remove:bold:x")]
    public async Task A_row_action_with_a_bad_row_number_is_answered_with_400(string url, string action)
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, _) = await Post(app.NewClient(), url, Merge(Assignment, action));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task Rows_exist_only_for_the_kinds_that_have_them()
    {
        using TestApp app = NewApp();

        Assert.Equal(HttpStatusCode.NotFound, (await Post(app.NewClient(), "/dev/content/skills/SKILL_A/rows", "row=add")).Status);
    }

    [Fact]
    public async Task A_datastar_row_post_patches_the_main_region()
    {
        using TestApp app = NewApp();

        (_, string stream) = await Post(app.NewClient(), "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", Merge(Assignment, "row=add"), datastar: true);

        Assert.Contains("data: elements <main id=\"main\"", stream);
        Assert.Contains("name=\"offer[2].count\"", stream);
    }

    // --- Page rules ---

    [Fact]
    public async Task Every_button_is_disabled_while_a_request_is_in_flight_and_the_form_is_a_datastar_form_posted_as_a_form()
    {
        using TestApp app = NewApp();

        string body = (await Get(app.NewClient(), "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1")).Body;

        Assert.Contains("data-on:submit=\"@post('/dev/content/assignments/ASSIGNMENT_TEMPLATE_1', {contentType: 'form'})\"", WebUtility.HtmlDecode(body));
        Assert.Contains("novalidate", body);
        foreach (Match button in Regex.Matches(body, "<button\\b[^>]*>"))
        {
            Assert.Contains("data-attr:disabled=", button.Value);
        }
    }

    [Fact]
    public async Task Delete_asks_again_in_the_page_before_it_posts()
    {
        using TestApp app = NewApp();

        string body = (await Get(app.NewClient(), "/dev/content/gear/GEAR_04")).Body;

        Assert.Contains("data-signals:_confirm=\"false\"", body);
        Assert.Matches("<button[^>]*id=\"delete-open\"[^>]*>Delete</button>", body);
        Assert.Matches("<button[^>]*data-on:click=\"\\$_confirm = false; document.getElementById\\(&#x27;delete-open&#x27;\\).focus\\(\\)\"[^>]*>Keep it</button>", body);
        Assert.Contains("data-show=\"$_confirm\"", body);
        Assert.Contains("@post('/dev/content/gear/GEAR_04/delete', {contentType: 'form'})", WebUtility.HtmlDecode(body));
        Assert.DoesNotContain("/dev/content/gear/new/delete", body);
    }

    [Fact]
    public async Task An_unnamed_item_shows_its_id_as_the_link_and_no_name_in_the_name_column_on_the_index_and_the_list()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        foreach (string url in new[] { "/dev/content", "/dev/content/skills" })
        {
            string body = (await Get(client, url)).Body;

            Assert.Contains("<a href=\"/dev/content/skills/SKILL_A\">SKILL_A</a> <span class=\"quiet\">No name</span>", body);
        }
    }

    [Fact]
    public async Task The_lossy_label_is_a_neutral_tag_not_the_caution_colour()
    {
        using TestApp app = NewApp(BreakGear);

        foreach (string url in new[] { "/dev/content", "/dev/content/gear" })
        {
            string body = (await Get(app.NewClient(), url)).Body;

            Assert.Contains("Has content the editor cannot keep", body);
            Assert.DoesNotContain("tag--caution", body);
        }
    }

    // The kind's only file does not parse, so a new item has nowhere to go: a refusal with the reason, not a 500.
    [Fact]
    public async Task A_create_when_the_kinds_only_file_does_not_parse_is_refused_in_words()
    {
        using TestApp app = NewApp(root => File.WriteAllText(Path.Combine(root, "gear", "base.yaml"), "- [\n"));
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/gear", "id=GEAR_09&tag=tools");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("gear/base.yaml cannot be read, so nothing can be added to it.", body);
        Assert.Equal(before, Everything(app));
    }

    // The file and the reason stay apart when copied or read out.
    [Fact]
    public async Task A_refusal_puts_a_separator_between_the_file_and_the_reason()
    {
        using TestApp app = NewApp();

        (_, string body) = await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/delete", "");

        Assert.Matches("<li><span class=\"m\">assignments/base\\.yaml</span>: ASSIGNMENT_TEMPLATE_1", body);
    }

    // The index says how games in progress learn that the content changed.
    [Fact]
    public async Task The_index_says_when_to_raise_the_pack_version()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content");

        Assert.Contains("Raise the version in the pack file when games in progress should show that the content has changed.", body);
    }

    // A file saved with a byte-order mark keeps it, whether the save is of a text file or an item.
    [Fact]
    public async Task A_byte_order_mark_is_kept_when_a_file_is_saved()
    {
        byte[] mark = [0xEF, 0xBB, 0xBF];
        using TestApp app = NewApp(root =>
        {
            foreach (string file in new[] { "pack.yaml", "skills/base.yaml" })
            {
                string path = Path.Combine(root, file);
                File.WriteAllBytes(path, [.. mark, .. File.ReadAllBytes(path)]);
            }
        });

        (HttpStatusCode text, _) = await Post(app.NewClient(), "/dev/content/files/pack", "text=" + Uri.EscapeDataString("id: CORE\nversion: 2\ncalendar: calendar_a\n"));
        (HttpStatusCode item, _) = await Post(app.NewClient(), "/dev/content/skills/SKILL_A", "name=NAME_PLACEHOLDER");

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (text, item));
        Assert.Equal(mark, File.ReadAllBytes(Path.Combine(app.ContentPath, "pack.yaml"))[..3]);
        Assert.Equal(mark, File.ReadAllBytes(Path.Combine(app.ContentPath, "skills", "base.yaml"))[..3]);
        Assert.Contains("version: 2", Disk(app, "pack.yaml"));
        Assert.Contains("NAME_PLACEHOLDER", Disk(app, "skills/base.yaml"));
    }

    // --- Same-origin posts ---

    [Theory]
    [InlineData("/dev/content/reload")]
    [InlineData("/dev/content/skills/SKILL_A")]
    [InlineData("/dev/content/skills")]
    [InlineData("/dev/content/gear/GEAR_04/delete")]
    [InlineData("/dev/content/assignments/rows")]
    public async Task A_post_from_another_site_is_refused_with_403_and_writes_nothing(string url)
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);

        foreach (string site in new[] { "cross-site", "same-site", "none" })
        {
            (HttpStatusCode status, _) = await Post(client, url, "id=SKILL_E&name=NAME_PLACEHOLDER", headers: h => h.Add("Sec-Fetch-Site", site));
            Assert.Equal(HttpStatusCode.Forbidden, status);
        }

        (HttpStatusCode foreign, _) = await Post(client, url, "id=SKILL_E&name=NAME_PLACEHOLDER", headers: h => h.Add("Origin", "http://evil.example"));
        (HttpStatusCode port, _) = await Post(client, url, "id=SKILL_E&name=NAME_PLACEHOLDER", headers: h => h.Add("Origin", "http://localhost:81"));
        (HttpStatusCode scheme, _) = await Post(client, url, "id=SKILL_E&name=NAME_PLACEHOLDER", headers: h => h.Add("Origin", "https://localhost"));
        (HttpStatusCode opaque, _) = await Post(client, url, "id=SKILL_E&name=NAME_PLACEHOLDER", headers: h => h.Add("Origin", "null"));

        Assert.All(new[] { foreign, port, scheme, opaque }, s => Assert.Equal(HttpStatusCode.Forbidden, s));
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_post_from_the_same_origin_and_a_post_with_neither_header_are_allowed()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode sameOrigin, _) = await Post(client, "/dev/content/skills/SKILL_A", "name=NAME_PLACEHOLDER_A", headers: h => h.Add("Sec-Fetch-Site", "same-origin"));
        (HttpStatusCode matching, _) = await Post(client, "/dev/content/skills/SKILL_B", "name=NAME_PLACEHOLDER_B", headers: h => h.Add("Origin", "http://localhost"));
        (HttpStatusCode neither, _) = await Post(client, "/dev/content/skills/SKILL_C", "name=NAME_PLACEHOLDER_C");
        (HttpStatusCode reload, _) = await Post(client, "/dev/content/reload", "", headers: h => h.Add("Sec-Fetch-Site", "same-origin"));

        Assert.All(new[] { sameOrigin, matching, neither, reload }, s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal("NAME_PLACEHOLDER_B", app.Live.Names.Skill("SKILL_B"));
    }

    // DNS rebinding: a page on a foreign name that resolves to this machine sends its own host and a matching origin.
    [Theory]
    [InlineData("GET", "/dev/content")]
    [InlineData("GET", "/dev/content/skills/SKILL_A")]
    [InlineData("POST", "/dev/content/reload")]
    [InlineData("POST", "/dev/content/skills/SKILL_A")]
    [InlineData("POST", "/dev/content/skills")]
    [InlineData("POST", "/dev/content/skills/rows")]
    [InlineData("POST", "/dev/content/gear/GEAR_04/delete")]
    [InlineData("POST", "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows")]
    [InlineData("POST", "/dev/content/files/pack")]
    public async Task A_request_to_a_foreign_host_is_refused_even_with_a_matching_origin(string method, string url)
    {
        using TestApp app = NewApp();
        string before = Everything(app);
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = new FormUrlEncodedContent(Fields("id=SKILL_E&name=NAME_PLACEHOLDER&text=id%3A%20CORE"));
        }

        request.Headers.Host = "evil.example";
        request.Headers.Add("Origin", "http://evil.example");

        HttpResponseMessage response = await app.NewClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, Everything(app));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("localhost:5000")]
    [InlineData("127.0.0.1:5000")]
    [InlineData("[::1]:5000")]
    public async Task The_loopback_host_names_are_allowed(string host)
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        var get = new HttpRequestMessage(HttpMethod.Get, "/dev/content");
        get.Headers.Host = host;
        var post = new HttpRequestMessage(HttpMethod.Post, "/dev/content/skills/SKILL_A") { Content = new FormUrlEncodedContent(Fields("name=NAME_PLACEHOLDER_A")) };
        post.Headers.Host = host;
        post.Headers.Add("Origin", $"http://{host}");

        HttpResponseMessage got = await client.SendAsync(get);
        HttpResponseMessage posted = await client.SendAsync(post);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (got.StatusCode, posted.StatusCode));
    }

    [Fact]
    public async Task A_get_from_another_site_is_not_refused()
    {
        using TestApp app = NewApp();
        var request = new HttpRequestMessage(HttpMethod.Get, "/dev/content/skills/SKILL_A");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        HttpResponseMessage response = await app.NewClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Focus after a response ---

    // The element that takes focus carries a data-init that calls focus(); one element per response.
    internal static List<string> Focused(string body)
    {
        return Regex.Matches(body, "<[^>]*data-init=\"[^\"]*focus[^\"]*\"[^>]*>").Select(m => Regex.Match(m.Value, "\\bid=\"([^\"]*)\"").Groups[1].Value).ToList();
    }

    [Fact]
    public async Task After_Add_focus_goes_to_the_first_field_of_the_new_row_and_after_Remove_to_the_Add_button()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string added) = await Post(client, "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", Merge(Assignment, "row=add"));
        (_, string removed) = await Post(client, "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", Merge(Assignment, "row=remove:0"));

        Assert.Equal(["f-offer-2-count"], Focused(added));
        Assert.Equal(["row-add"], Focused(removed));
    }

    [Fact]
    public async Task After_a_refusal_focus_goes_to_the_first_field_with_an_error_or_to_the_alert_for_the_loaders_words()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string field) = await Post(client, "/dev/content/crew/CREW_01", Crew("caution=9&skills.SKILL_A=99"));
        (_, string loader) = await Post(client, "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "name=NAME_PLACEHOLDER&loadoutLimit=2&offer[0].count=3&offer[0].weight=1");
        string page = (await Get(client, "/dev/content/crew/CREW_01")).Body;
        (_, string saved) = await Post(client, "/dev/content/crew/CREW_01", Crew());

        Assert.Equal(["f-caution"], Focused(field));
        Assert.Equal(["form-alert"], Focused(loader));
        Assert.Matches("<div[^>]*id=\"form-alert\"[^>]*tabindex=\"-1\"|<div[^>]*tabindex=\"-1\"[^>]*id=\"form-alert\"", loader);
        Assert.Empty(Focused(page));
        Assert.Empty(Focused(saved));
    }

    // --- Rows with a gap in the indexes ---

    [Fact]
    public async Task Rows_posted_with_a_gap_in_their_indexes_are_renumbered_so_an_error_points_at_a_field_on_the_page()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode refused, string body) = await Post(client, "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "name=NAME_PLACEHOLDER&loadoutLimit=2&offer[0].count=1&offer[0].weight=6&offer[5].count=2&offer[5].weight=0");
        (HttpStatusCode saved, _) = await Post(client, "/dev/content/assignments/ASSIGNMENT_TEMPLATE_1", "name=NAME_PLACEHOLDER&loadoutLimit=2&offer[0].count=1&offer[0].weight=6&offer[5].count=2&offer[5].weight=3");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused);
        Assert.Contains("id=\"f-offer-1-weight-error\"", body);
        Assert.Contains("name=\"offer[1].weight\"", body);
        Assert.DoesNotContain("offer[5]", body);
        Assert.Equal(HttpStatusCode.OK, saved);
        Assert.Equal(2, app.Live.Pack.Assignments.Single().Offer.Count);
    }

    // --- A refused save beside a lossy item ---

    [Fact]
    public async Task The_loss_message_shows_once_on_a_refused_save_in_the_alert_and_not_also_in_the_warning()
    {
        using TestApp app = NewApp(BreakGear);

        (_, string body) = await Post(app.NewClient(), "/dev/content/gear/GEAR_02", "name=NAME_PLACEHOLDER&tag=tools");

        Assert.Single(Regex.Matches(body, "GEAR_01 has a key the editor cannot keep"));
    }

    // --- Names that need quoting, and the edges of the name and id rules ---

    [Theory]
    [InlineData("a: b")]
    [InlineData("# not a comment")]
    [InlineData("- leading dash")]
    [InlineData("say \"hi\" and 'bye'")]
    [InlineData("caf\u00e9 \u65e5\u672c \ud83d\ude80")]
    [InlineData("yes")]
    [InlineData("123")]
    public async Task A_name_that_needs_quoting_is_read_back_from_the_file_exactly(string name)
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/skills/SKILL_A", "name=" + Uri.EscapeDataString(name));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(name, ContentEdits.Get<SkillModel>(app.Editor.Read(), ContentKind.Skill, "SKILL_A")!.Name);
        Assert.Equal(name, app.Live.Names.Skill("SKILL_A"));
    }

    [Fact]
    public async Task A_name_of_exactly_80_characters_is_accepted_and_81_is_not()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode ok, _) = await Post(client, "/dev/content/skills/SKILL_A", "name=" + new string('n', 80));
        (HttpStatusCode tooLong, _) = await Post(client, "/dev/content/skills/SKILL_A", "name=" + new string('n', 81));

        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong);
        Assert.Equal(new string('n', 80), app.Live.Names.Skill("SKILL_A"));
    }

    [Theory]
    [InlineData("SKILL_E%0A")]
    [InlineData("SKILL%0AE")]
    public async Task An_id_with_a_trailing_or_an_embedded_line_break_is_refused(string id)
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/skills", "id=" + id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains(Ids.ItemRule, body);
        Assert.Equal(before, Everything(app));
    }

    // --- The repository's own content ---

    [Fact]
    public async Task The_repositorys_own_content_is_byte_identical_after_saves_creates_and_deletes()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        await Post(client, "/dev/content/skills/SKILL_A", "name=NAME_PLACEHOLDER");
        await Post(client, "/dev/content/gear", "id=GEAR_05&tag=medical");
        await Post(client, "/dev/content/gear/GEAR_05/delete", "");

        guard.Verify();
    }
}

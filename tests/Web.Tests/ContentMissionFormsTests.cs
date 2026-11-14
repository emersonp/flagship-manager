using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// The forms for objective templates and encounters, through the real app on a scratch copy of the content.
public sealed class ContentMissionFormsTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private const string Objective1 =
        "name=&tags=science&teamSizeMin=2&teamSizeMax=4&need=4&unattended=6"
        + "&stages[0].slot=approach&stages[0].draw=field&stages[0].countMin=1&stages[0].countMax=1"
        + "&stages[1].slot=complication&stages[1].draw=field&stages[1].countMin=2&stages[1].countMax=2"
        + "&stages[2].slot=objective&stages[2].draw=field&stages[2].countMin=1&stages[2].countMax=1"
        + "&stages[3].slot=extraction&stages[3].draw=field&stages[3].countMin=1&stages[3].countMax=1&stages[3].skippable=true";

    private const string Encounter1 =
        "name=&tags=field, hazard&chain=ENCOUNTER_CHAIN"
        + "&careful.skill=SKILL_A&careful.difficulty=5&careful.gearTag=scanner"
        + "&fast.skill=SKILL_C&fast.difficulty=6&fast.gearTag="
        + "&hasDecision=true&decision.name=&decision.stakes=1&bold[0].momentum=1&bold[1].strain=1";

    private const string EncounterNone =
        "name=&tags=field&chain="
        + "&careful.skill=SKILL_A&careful.difficulty=5&careful.gearTag="
        + "&fast.skill=SKILL_B&fast.difficulty=6&fast.gearTag=";

    // The posted fields with the overrides replacing every field of the same name.
    private static string Merge(string baseFields, string overrides)
    {
        List<KeyValuePair<string, string>> fields = Fields(baseFields);
        List<KeyValuePair<string, string>> over = Fields(overrides);
        foreach (string key in over.Select(o => o.Key).Distinct())
        {
            fields.RemoveAll(f => f.Key == key);
        }

        fields.AddRange(over);
        return string.Join("&", fields.Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
    }

    private static string Without(string fields, string prefix)
    {
        return string.Join("&", Fields(fields).Where(f => !f.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
    }

    // Every named field a browser would post from the item form: text and hidden inputs, ticked boxes, and the chosen option.
    private static string FormFields(string body)
    {
        string form = Regex.Match(body, "<form id=\"item-form\".*?</form>", RegexOptions.Singleline).Value;
        var fields = new List<string>();
        foreach (Match input in Regex.Matches(form, "<input\\b[^>]*>"))
        {
            string tag = input.Value;
            string name = Regex.Match(tag, "\\bname=\"([^\"]*)\"").Groups[1].Value;
            string type = Regex.Match(tag, "\\btype=\"([^\"]*)\"").Groups[1].Value;
            if (name.Length == 0 || (type is "checkbox" or "radio" && !Regex.IsMatch(tag, "\\bchecked\\b")))
            {
                continue;
            }

            Match value = Regex.Match(tag, "\\bvalue=\"([^\"]*)\"");
            fields.Add($"{Uri.EscapeDataString(WebUtility.HtmlDecode(name))}={Uri.EscapeDataString(WebUtility.HtmlDecode(value.Success ? value.Groups[1].Value : "on"))}");
        }

        foreach (Match select in Regex.Matches(form, "<select\\b[^>]*\\bname=\"([^\"]*)\"[^>]*>(.*?)</select>", RegexOptions.Singleline))
        {
            Match chosen = Regex.Match(select.Groups[2].Value, "<option\\b[^>]*\\bvalue=\"([^\"]*)\"[^>]*\\bselected\\b");
            fields.Add($"{Uri.EscapeDataString(select.Groups[1].Value)}={Uri.EscapeDataString(WebUtility.HtmlDecode(chosen.Groups[1].Value))}");
        }

        return string.Join("&", fields);
    }

    private static string PackJson(TestApp app)
    {
        return JsonSerializer.Serialize(app.Live.Pack, SimJson.Options);
    }

    private static string Text(string body)
    {
        return WebUtility.HtmlDecode(body);
    }

    // --- The forms' values ---

    [Fact]
    public async Task The_objective_form_shows_every_value_with_the_stages_in_order_and_what_each_stage_matches()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Get(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_2");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("repair", Value(body, "tags"));
        Assert.Equal("2", Value(body, "teamSizeMin"));
        Assert.Equal("3", Value(body, "teamSizeMax"));
        Assert.Equal("3", Value(body, "need"));
        Assert.Equal("5", Value(body, "unattended"));
        Assert.Equal("approach", Value(body, "stages[0].slot"));
        Assert.Equal("objective", Value(body, "stages[1].slot"));
        Assert.Equal("extraction", Value(body, "stages[2].slot"));
        Assert.Equal("1", Value(body, "stages[1].countMin"));
        Assert.Equal("2", Value(body, "stages[1].countMax"));
        Assert.Equal(["field", "hazard"], Checked(body, "stages[0].draw"));
        Assert.Equal(["technical"], Checked(body, "stages[1].draw"));
        Assert.Equal(["true"], Checked(body, "stages[2].skippable"));
        Assert.Empty(Checked(body, "stages[0].skippable"));
        Assert.Contains("3 encounters match", body);
        Assert.Contains("2 encounters match", body);
    }

    [Fact]
    public async Task The_objective_form_offers_the_tags_in_use_as_suggestions_and_says_the_rule()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1");

        Assert.Equal("science", Value(body, "tags"));
        Assert.Matches("<button[^>]*data-tag=\"repair\"[^>]*>", body);
        Assert.Matches("<button[^>]*data-tag=\"science\"[^>]*>", body);
        Assert.Contains("Separate tags with commas. Tags are matched exactly, so case matters.", body);
    }

    [Fact]
    public async Task The_encounter_form_shows_every_value_including_the_chain_the_gear_tag_the_decision_and_its_effects()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode status, string body) = await Get(client, "/dev/content/encounters/ENCOUNTER_02");
        (_, string blocked) = await Get(client, "/dev/content/encounters/ENCOUNTER_06");
        (_, string plain) = await Get(client, "/dev/content/encounters/ENCOUNTER_04");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("field, social", Value(body, "tags"));
        Assert.Equal("ENCOUNTER_CHAIN", Selected(body, "chain"));
        Assert.Equal("SKILL_D", Selected(body, "careful.skill"));
        Assert.Equal("5", Value(body, "careful.difficulty"));
        Assert.Equal("", Selected(body, "careful.gearTag"));
        Assert.Equal("SKILL_B", Selected(body, "fast.skill"));
        Assert.Equal("6", Value(body, "fast.difficulty"));
        Assert.Equal(["true"], Checked(body, "hasDecision"));
        Assert.Contains("DECISION_PROMPT_2", body);
        Assert.Null(Value(body, "decision.id"));
        Assert.Equal("2", Value(body, "decision.stakes"));
        Assert.Equal("1", Value(body, "bold[0].progress"));
        Assert.Equal("light", Selected(body, "bold[1].injury"));
        Assert.Equal("-1", Value(body, "safe[0].strain"));
        Assert.Empty(Checked(body, "commsBlocked"));
        Assert.Equal(["true"], Checked(blocked, "commsBlocked"));
        Assert.Empty(Checked(plain, "hasDecision"));
        Assert.DoesNotContain("name=\"decision.stakes\"", plain);
    }

    [Fact]
    public async Task A_new_encounter_form_asks_for_the_id_and_the_chain_offers_none_and_every_encounter()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content/encounters/new");

        Assert.NotNull(Value(body, "id"));
        Match chain = Regex.Match(body, "<select\\b[^>]*\\bname=\"chain\"[^>]*>(.*?)</select>", RegexOptions.Singleline);
        Assert.Contains(">None</option>", chain.Value);
        Assert.Contains("value=\"ENCOUNTER_01\"", chain.Value);
        Assert.Contains("value=\"ENCOUNTER_CHAIN\"", chain.Value);
    }

    // --- Nothing the YAML holds is lost ---

    [Fact]
    public async Task Opening_and_saving_every_objective_and_encounter_unchanged_leaves_the_pack_and_the_files_as_they_were()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);
        string pack = PackJson(app);
        var items = app.Editor.Read().Index().Where(i => i.Kind is ContentKind.Objective or ContentKind.Encounter).ToList();
        Assert.True(items.Count >= 10);

        foreach (ContentItemRef item in items)
        {
            string url = $"/dev/content/{ContentKinds.Segment(item.Kind)}/{item.Id}";
            (_, string body) = await Get(client, url);
            (HttpStatusCode status, string saved) = await Post(client, url, FormFields(body));
            Assert.True(status == HttpStatusCode.OK, $"{item.Id}: {status} {Regex.Match(saved, "class=\"alert\".*?</div>", RegexOptions.Singleline).Value}");
        }

        Assert.Equal(pack, PackJson(app));
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task An_effect_entry_with_several_keys_and_a_withdraw_survive_a_save()
    {
        using TestApp app = NewApp(root =>
        {
            string file = Path.Combine(root, "encounters", "base.yaml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("bold: [{ momentum: 1 }, { strain: 1 }]", "bold: [{ momentum: 1, strain: 2 }, { withdraw: true }, { progress: 1 }, { progress: 2 }]"));
        });
        string before = PackJson(app);
        HttpClient client = app.NewClient();

        (_, string body) = await Get(client, "/dev/content/encounters/ENCOUNTER_01");
        (HttpStatusCode status, _) = await Post(client, "/dev/content/encounters/ENCOUNTER_01", FormFields(body));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(before, PackJson(app));
        Assert.Contains("bold: [{ momentum: 1, strain: 2 }, { withdraw: true }, { progress: 1 }, { progress: 2 }]", Disk(app, "encounters/base.yaml"));
    }

    // --- Saving, creating, deleting ---

    [Fact]
    public async Task Saving_an_objective_changes_the_name_and_a_number_and_the_game_shows_both()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Merge(Objective1, "name=NAME_PLACEHOLDER&need=3"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Saved.", body);
        string file = Disk(app, "objectives/base.yaml");
        Assert.Contains("name: NAME_PLACEHOLDER", file);
        Assert.Contains("need: 3", file);
        ObjectiveDef live = app.Live.Pack.Objectives.Single(o => o.Id == "OBJECTIVE_TEMPLATE_1");
        Assert.Equal("NAME_PLACEHOLDER", live.Name);
        Assert.Equal(3, live.Need);
    }

    [Fact]
    public async Task Saving_an_encounter_changes_the_name_the_chain_an_approach_and_the_decision()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", Merge(Encounter1, "name=NAME_PLACEHOLDER&chain=&careful.difficulty=4&careful.gearTag=tools&decision.stakes=3&decision.name=NAME_PLACEHOLDER_2"));

        Assert.Equal(HttpStatusCode.OK, status);
        EncounterDef live = app.Live.Pack.Encounters.Single(e => e.Id == "ENCOUNTER_01");
        Assert.Equal("NAME_PLACEHOLDER", live.Name);
        Assert.Null(live.Chain);
        Assert.Equal(4, live.Careful.Difficulty);
        Assert.Equal("tools", live.Careful.GearTag);
        Assert.Equal(3, live.Decision!.Stakes);
        Assert.Equal("NAME_PLACEHOLDER_2", live.Decision.Name);
        Assert.Equal("DECISION_PROMPT_1", live.Decision.Id);
    }

    [Fact]
    public async Task Creating_an_objective_and_an_encounter_writes_each_and_answers_with_its_form()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode objective, string objectiveBody) = await Post(client, "/dev/content/objectives", "id=OBJECTIVE_NEW_1&" + Objective1.Replace("name=&", "name=NAME_PLACEHOLDER&"));
        (HttpStatusCode encounter, string encounterBody) = await Post(client, "/dev/content/encounters", "id=ENCOUNTER_NEW_1&" + Merge(EncounterNone, "hasDecision=true&decision.id=DECISION_NEW_1&decision.stakes=2&bold[0].progress=1&safe[0].strain=-1"));

        Assert.Equal(HttpStatusCode.OK, objective);
        Assert.Equal(HttpStatusCode.OK, encounter);
        Assert.Contains("Saved.", objectiveBody);
        Assert.Contains("Saved.", encounterBody);
        Assert.Contains("/dev/content/objectives/OBJECTIVE_NEW_1", Text(objectiveBody));
        Assert.Contains(app.Live.Pack.Objectives, o => o.Id == "OBJECTIVE_NEW_1");
        EncounterDef made = app.Live.Pack.Encounters.Single(e => e.Id == "ENCOUNTER_NEW_1");
        Assert.Equal("DECISION_NEW_1", made.Decision!.Id);
        Assert.Equal(1, made.Decision.Bold.Progress);
        Assert.Equal(-1, made.Decision.Safe.Strain);
    }

    [Fact]
    public async Task Deleting_an_encounter_that_nothing_chains_to_removes_it()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        await Post(client, "/dev/content/encounters", "id=ENCOUNTER_NEW_1&" + EncounterNone);

        (HttpStatusCode encounter, string list) = await Post(client, "/dev/content/encounters/ENCOUNTER_NEW_1/delete", "");

        Assert.Equal(HttpStatusCode.OK, encounter);
        Assert.Contains("Deleted ENCOUNTER_NEW_1.", list);
        Assert.DoesNotContain(app.Live.Pack.Encounters, e => e.Id == "ENCOUNTER_NEW_1");
    }

    [Fact]
    public async Task Deleting_an_encounter_another_one_chains_to_is_the_loaders_refusal_and_deletes_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_CHAIN/delete", "");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("Chains to unknown encounter &#x27;ENCOUNTER_CHAIN&#x27;.", body);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task Deleting_an_objective_an_assignment_still_needs_is_refused_and_deletes_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/delete", "");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("offers up to 2 objectives", Text(body));
        Assert.Contains(app.Live.Pack.Objectives, o => o.Id == "OBJECTIVE_TEMPLATE_1");
        Assert.Equal(before, Everything(app));
    }

    // --- Field checks ---

    public static TheoryData<string, string, string, string> FieldCases => new()
    {
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "need=", "Enter a whole number." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "need=0", "Must be 1 or more." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "teamSizeMin=x", "Must be a whole number." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "teamSizeMax=1", "The maximum must not be below the minimum." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "teamSizeMin=0", "Must be 1 or more." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "unattended=0", "Must be 1 or more." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "stages[0].slot=", "Enter a slot." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "stages[1].countMax=1", "The maximum must not be below the minimum." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "stages[1].countMin=0", "Must be 1 or more." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "stages[2].draw=nothing_uses_this", "Unknown tag." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "tags=a,,b", "A tag cannot be blank." },
        { "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Objective1, "name=" + new string('x', 81), "A name is 1 to 80 characters with no line break." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "careful.skill=", "Choose a skill." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "fast.skill=SKILL_NOT_THERE", "Unknown skill." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "careful.difficulty=0", "Must be 1 or more." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "fast.difficulty=", "Enter a whole number." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "careful.gearTag=not_a_tag", "Unknown gear tag." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "chain=ENCOUNTER_NOT_THERE", "Unknown encounter." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "chain=ENCOUNTER_01", "Chains to itself." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "decision.stakes=6", "Must be 1 to 5." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "decision.stakes=", "Enter a whole number." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "bold[0].momentum=x", "Must be a whole number." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "bold[1].strain=&bold[1].injury=", "Give the effect a value." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "bold[0].injury=medium", "Choose light or severe." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "tags=field;hazard,a\nb", "A tag is one line." },
    };

    [Theory]
    [MemberData(nameof(FieldCases))]
    public async Task A_field_that_fails_its_check_is_explained_beside_it_and_nothing_is_written(string url, string fields, string overrides, string words)
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), url, Merge(fields, overrides));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Matches("<p class=\"error\" id=\"f-[A-Za-z0-9_-]+-error\">" + Regex.Escape(words), body);
        Assert.Matches("aria-describedby=\"[^\"]*f-[A-Za-z0-9_-]+-error", body);
        Assert.Contains("aria-invalid=\"true\"", body);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task The_team_size_error_is_beside_the_maximum_and_a_stage_count_error_beside_that_stages_maximum()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string team) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Merge(Objective1, "teamSizeMax=1"));
        (_, string stage) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Merge(Objective1, "stages[1].countMax=1"));

        Assert.Contains("id=\"f-teamSizeMax-error\"", team);
        Assert.DoesNotContain("id=\"f-teamSizeMin-error\"", team);
        Assert.Contains("id=\"f-stages-1-countMax-error\"", stage);
        Assert.Equal(["f-teamSizeMax"], Focused(team));
        Assert.Equal(["f-stages-1-countMax"], Focused(stage));
    }

    [Fact]
    public async Task An_objective_with_no_stages_is_refused_beside_the_stages()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Without(Objective1, "stages["));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("An objective needs at least one stage.", body);
    }

    [Fact]
    public async Task A_stage_that_draws_more_than_the_pack_holds_is_the_loaders_refusal_in_the_alert_with_nothing_written()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Merge(Objective1, "stages[0].draw=social&stages[0].draw=technical&stages[0].countMax=3"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("draws up to 3 encounters tagged [social, technical], but only 0 match.", Text(body));
        Assert.Matches("<div id=\"form-alert\"[^>]*>[^<]*(<[^>]*>[^<]*)*The change was refused", body);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_refused_form_keeps_every_row_and_value_that_was_typed()
    {
        using TestApp app = NewApp();

        (_, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", Merge(Encounter1, "careful.difficulty=0&bold[1].strain=7&tags=typed_tag"));

        Assert.Equal("typed_tag", Value(body, "tags"));
        Assert.Equal("7", Value(body, "bold[1].strain"));
        Assert.Equal("0", Value(body, "careful.difficulty"));
    }

    [Fact]
    public async Task Tags_are_trimmed_kept_in_the_case_typed_and_written_once_each()
    {
        using TestApp app = NewApp();

        await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Merge(Objective1, "tags= science ,  Repair ,science"));

        Assert.Equal(["science", "Repair"], app.Live.Pack.Objectives.Single(o => o.Id == "OBJECTIVE_TEMPLATE_1").Tags);
    }

    [Fact]
    public async Task Html_in_a_name_is_shown_as_text_and_a_posted_id_on_an_existing_item_is_ignored()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string body) = await Post(client, "/dev/content/encounters/ENCOUNTER_04", "id=ENCOUNTER_09&" + Merge(EncounterNone, "name=<b>x</b>"));

        Assert.DoesNotContain("<b>x</b>", body);
        Assert.Contains("&lt;b&gt;x&lt;/b&gt;", body);
        Assert.DoesNotContain(app.Live.Pack.Encounters, e => e.Id == "ENCOUNTER_09");
    }

    // --- Stage rows ---

    [Fact]
    public async Task Adding_a_stage_re_renders_from_the_posted_fields_with_an_empty_stage_and_saves_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "need=2&row=add"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("2", Value(body, "need"));
        Assert.Equal("", Value(body, "stages[4].slot"));
        Assert.Equal("1", Value(body, "stages[4].countMin"));
        Assert.Null(Value(body, "stages[5].slot"));
        Assert.DoesNotContain("Saved.", body);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task Removing_a_stage_drops_it_and_the_others_keep_their_order()
    {
        using TestApp app = NewApp();

        (_, string body) = await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=remove:1"));

        Assert.Equal("approach", Value(body, "stages[0].slot"));
        Assert.Equal("objective", Value(body, "stages[1].slot"));
        Assert.Equal("extraction", Value(body, "stages[2].slot"));
        Assert.Null(Value(body, "stages[3].slot"));
        Assert.Equal(["true"], Checked(body, "stages[2].skippable"));
    }

    [Fact]
    public async Task Moving_a_stage_up_or_down_swaps_it_with_its_neighbour_and_the_ends_do_not_move()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string up) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=up:2"));
        (_, string down) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=down:0"));
        (_, string top) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=up:0"));
        (_, string bottom) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=down:3"));

        Assert.Equal(["approach", "objective", "complication", "extraction"], Enumerable.Range(0, 4).Select(i => Value(up, $"stages[{i}].slot")));
        Assert.Equal("2", Value(up, "stages[2].countMin"));
        Assert.Equal(["complication", "approach", "objective", "extraction"], Enumerable.Range(0, 4).Select(i => Value(down, $"stages[{i}].slot")));
        Assert.Equal(["approach", "complication", "objective", "extraction"], Enumerable.Range(0, 4).Select(i => Value(top, $"stages[{i}].slot")));
        Assert.Equal(["approach", "complication", "objective", "extraction"], Enumerable.Range(0, 4).Select(i => Value(bottom, $"stages[{i}].slot")));
    }

    [Fact]
    public async Task A_moved_stage_keeps_its_own_tags_and_skippable_and_the_saved_file_has_the_stages_in_the_shown_order()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        (_, string moved) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "stages[0].draw=field&stages[0].draw=hazard&row=down:0"));

        (HttpStatusCode status, _) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", FormFields(moved));

        Assert.Equal(HttpStatusCode.OK, status);
        ObjectiveDef live = app.Live.Pack.Objectives.Single(o => o.Id == "OBJECTIVE_TEMPLATE_1");
        Assert.Equal(["complication", "approach", "objective", "extraction"], live.Stages.Select(s => s.Slot));
        Assert.Equal(["field", "hazard"], live.Stages[1].DrawTags);
        Assert.True(live.Stages[3].Skippable);
        string file = Disk(app, "objectives/base.yaml");
        Assert.True(file.IndexOf("slot: complication", StringComparison.Ordinal) < file.IndexOf("slot: approach", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_new_objectives_stage_rows_use_the_new_route()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/objectives/rows", "id=OBJECTIVE_NEW_1&" + Objective1 + "&row=add");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("OBJECTIVE_NEW_1", Value(body, "id"));
        Assert.Equal("", Value(body, "stages[4].slot"));
    }

    [Fact]
    public async Task The_stage_buttons_say_which_row_they_act_on_and_the_ends_cannot_move_off_the_list()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1");

        Assert.Contains("aria-label=\"Move stage 2 up\"", body);
        Assert.Contains("aria-label=\"Move stage 2 down\"", body);
        Assert.Contains("aria-label=\"Remove stage 2\"", body);
        Assert.Contains("value=\"up:1\"", body);
        Assert.Contains("value=\"down:1\"", body);
        Assert.Matches("<button[^>]*id=\"row-up-0\"[^>]*data-attr:disabled=\"true\"", body);
        Assert.Matches("<button[^>]*id=\"row-down-3\"[^>]*data-attr:disabled=\"true\"", body);
        Assert.Matches("<button[^>]*id=\"row-add\"[^>]*>Add stage</button>", body);
    }

    // --- The match count beside a stage ---

    [Fact]
    public async Task The_match_count_follows_the_draw_tags_as_the_game_draws_and_never_counts_a_chain_target()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string hazard) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "stages[0].draw=field&stages[0].draw=hazard&row=add"));
        (_, string chained) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "stages[0].draw=chained&row=add"));
        (_, string any) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "stages[0].draw=&row=add"));

        Assert.Contains("<p class=\"match\" id=\"stage-0-match\" role=\"status\">3 encounters match</p>", hazard);
        Assert.Contains("<p class=\"match match--none\" id=\"stage-0-match\" role=\"status\">No encounters match</p>", chained);
        Assert.Contains("id=\"stage-0-match\" role=\"status\">8 encounters match</p>", any);
        Assert.Contains("id=\"stage-4-match\" role=\"status\">8 encounters match</p>", any);
    }

    // The count is drawn when the form is, so the page says so beside each stage.
    [Fact]
    public async Task Each_stage_says_when_its_match_count_was_taken()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1");

        Assert.Equal(4, Regex.Matches(body, "Counted when the form was last refreshed\\.").Count);
    }

    [Fact]
    public async Task One_match_says_encounter_not_encounters()
    {
        using TestApp app = NewApp(root =>
        {
            string file = Path.Combine(root, "encounters", "base.yaml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("tags: [field, technical]\n  chain: ENCOUNTER_CHAIN\n  approaches:\n    careful: { skill: SKILL_B, difficulty: 5, gearTag: tools }", "tags: [field, technical, rare]\n  chain: ENCOUNTER_CHAIN\n  approaches:\n    careful: { skill: SKILL_B, difficulty: 5, gearTag: tools }"));
        });

        (_, string body) = await Post(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "stages[0].draw=rare&row=add"));

        Assert.Contains("id=\"stage-0-match\" role=\"status\">1 encounter matches</p>", body);
    }

    [Fact]
    public async Task The_stage_draw_tags_are_the_tags_encounters_use()
    {
        using TestApp app = NewApp();

        (_, string body) = await Get(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1");

        foreach (string tag in new[] { "field", "hazard", "social", "technical", "chained" })
        {
            Assert.Matches($"<input[^>]*name=\"stages\\[0\\]\\.draw\"[^>]*value=\"{tag}\"", body);
        }
    }

    // --- Effect rows and the decision ---

    [Fact]
    public async Task Adding_and_removing_effects_works_on_each_list_and_keeps_the_other()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);

        (_, string added) = await Post(client, "/dev/content/encounters/ENCOUNTER_01/rows", Merge(Encounter1, "row=add:safe"));
        (_, string removed) = await Post(client, "/dev/content/encounters/ENCOUNTER_01/rows", Merge(Encounter1, "row=remove:bold:0"));

        Assert.Equal("", Value(added, "safe[0].progress"));
        Assert.Equal("1", Value(added, "bold[0].momentum"));
        Assert.Equal("1", Value(added, "bold[1].strain"));
        Assert.Equal("1", Value(removed, "bold[0].strain"));
        Assert.Null(Value(removed, "bold[1].strain"));
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task Effect_rows_save_in_the_shown_order_with_every_key_and_the_withdraw()
    {
        using TestApp app = NewApp();
        string fields = Merge(Without(Encounter1, "bold["), "bold[0].progress=2&bold[0].momentum=-1&bold[1].injury=severe&bold[2].withdraw=true&safe[0].strain=-2");

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", fields);

        Assert.Equal(HttpStatusCode.OK, status);
        DecisionDef decision = app.Live.Pack.Encounters.Single(e => e.Id == "ENCOUNTER_01").Decision!;
        Assert.Equal(2, decision.Bold.Progress);
        Assert.Equal(-1, decision.Bold.Momentum);
        Assert.Equal(InjurySeverity.Severe, decision.Bold.InjuryRoll);
        Assert.True(decision.Bold.Withdraw);
        Assert.Equal(-2, decision.Safe.Strain);
        Assert.Contains("bold: [{ progress: 2, momentum: -1 }, { injuryRoll: severe }, { withdraw: true }]", Disk(app, "encounters/base.yaml"));
    }

    [Fact]
    public async Task Turning_the_decision_off_hides_its_fields_keeps_them_for_turning_it_on_again_and_saving_without_removes_it()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string before = Everything(app);

        (_, string off) = await Post(client, "/dev/content/encounters/ENCOUNTER_01/rows", Without(Encounter1, "hasDecision"));
        (_, string on) = await Post(client, "/dev/content/encounters/ENCOUNTER_04/rows", EncounterNone + "&hasDecision=true");
        (HttpStatusCode status, _) = await Post(client, "/dev/content/encounters/ENCOUNTER_01", Without(Encounter1, "hasDecision"));

        Assert.Empty(Checked(off, "hasDecision"));
        Assert.DoesNotMatch("<input\\b[^>]*type=\"text\"[^>]*name=\"decision.stakes\"", off);
        Assert.Matches("<input\\b[^>]*type=\"hidden\"[^>]*name=\"decision.stakes\"[^>]*value=\"1\"", off);
        Assert.Matches("<input\\b[^>]*type=\"hidden\"[^>]*name=\"bold\\[1\\].strain\"[^>]*value=\"1\"", off);
        Assert.Equal(["true"], Checked(on, "hasDecision"));
        Assert.NotNull(Value(on, "decision.id"));
        Assert.Equal("1", Value(on, "decision.stakes"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Null(app.Live.Pack.Encounters.Single(e => e.Id == "ENCOUNTER_01").Decision);
        Assert.NotEqual(before, Everything(app));
    }

    [Fact]
    public async Task A_decision_added_to_an_encounter_takes_a_typed_id_that_is_fixed_once_saved()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (HttpStatusCode first, _) = await Post(client, "/dev/content/encounters/ENCOUNTER_04", Merge(EncounterNone, "hasDecision=true&decision.id=DECISION_ADDED_1&decision.stakes=2&bold[0].progress=1"));
        (_, string reopened) = await Get(client, "/dev/content/encounters/ENCOUNTER_04");
        (_, string again) = await Post(client, "/dev/content/encounters/ENCOUNTER_04", Merge(EncounterNone, "hasDecision=true&decision.id=DECISION_OTHER_2&decision.stakes=3&bold[0].progress=1"));

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Null(Value(reopened, "decision.id"));
        Assert.Contains("DECISION_ADDED_1", reopened);
        Assert.Equal("DECISION_ADDED_1", app.Live.Pack.Encounters.Single(e => e.Id == "ENCOUNTER_04").Decision!.Id);
        Assert.Equal(3, app.Live.Pack.Encounters.Single(e => e.Id == "ENCOUNTER_04").Decision!.Stakes);
        Assert.DoesNotContain("DECISION_OTHER_2", again);
    }

    [Theory]
    [InlineData("", "Enter an id.")]
    [InlineData("decision_lower", "An id must be upper case letters, digits, and underscores, starting with a letter.")]
    [InlineData("DECISION_PROMPT_2", "That id is already used by ENCOUNTER_02.")]
    [InlineData("DECISION_ABORT", "That id is reserved for the abort call.")]
    public async Task A_new_decisions_id_is_checked_beside_the_field(string id, string words)
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_04", Merge(EncounterNone, $"hasDecision=true&decision.id={id}&decision.stakes=2"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Matches("<p class=\"error\" id=\"f-decision-id-error\">" + Regex.Escape(words), body);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task A_decision_id_taken_between_the_check_and_the_save_is_refused_under_the_lock()
    {
        using TestApp app = NewApp();
        app.Editor.AfterGate = () =>
        {
            app.Editor.AfterGate = null;
            ContentFileSet files = app.Editor.Read();
            EncounterModel other = ContentEdits.Get<EncounterModel>(files, ContentKind.Encounter, "ENCOUNTER_07")!;
            ContentEdits.Write(app.ContentPath, files, ContentEdits.Upsert(files, ContentKind.Encounter, other with { Decision = new DecisionModel("DECISION_RACE_1", null, 1, [], []) }));
        };

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_04", Merge(EncounterNone, "hasDecision=true&decision.id=DECISION_RACE_1&decision.stakes=2&bold[0].progress=1"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("That id is already used by ENCOUNTER_07.", body);
        Assert.Null(ContentEdits.Get<EncounterModel>(app.Editor.Read(), ContentKind.Encounter, "ENCOUNTER_04")!.Decision);
    }

    [Fact]
    public async Task Toggling_the_decision_posts_to_the_rows_route_and_the_response_focuses_the_box()
    {
        using TestApp app = NewApp();

        (_, string form) = await Get(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01");
        (_, string toggled) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01/rows?focus=f-hasDecision", Without(Encounter1, "hasDecision"));

        Assert.Contains("data-on:change=\"@post('/dev/content/encounters/ENCOUNTER_01/rows?focus=f-hasDecision', {contentType: 'form'})\"", Text(form));
        Assert.Equal(["f-hasDecision"], Focused(toggled));
    }

    // --- Focus ---

    [Fact]
    public async Task After_Add_focus_goes_to_the_new_rows_first_field_after_Remove_to_that_lists_Add_and_after_a_move_to_the_moved_rows_first_field()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string stage) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=add"));
        (_, string removed) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=remove:0"));
        (_, string moved) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=down:1"));
        (_, string effect) = await Post(client, "/dev/content/encounters/ENCOUNTER_01/rows", Merge(Encounter1, "row=add:safe"));
        (_, string effectRemoved) = await Post(client, "/dev/content/encounters/ENCOUNTER_01/rows", Merge(Encounter1, "row=remove:bold:0"));

        Assert.Equal(["f-stages-4-slot"], Focused(stage));
        Assert.Equal(["row-add"], Focused(removed));
        Assert.Equal(["f-stages-2-slot"], Focused(moved));
        Assert.Equal(["f-safe-0-progress"], Focused(effect));
        Assert.Equal(["row-add-bold"], Focused(effectRemoved));
    }

    // --- Page rules ---

    [Theory]
    [InlineData("/dev/content/objectives/OBJECTIVE_TEMPLATE_1")]
    [InlineData("/dev/content/encounters/ENCOUNTER_01")]
    [InlineData("/dev/content/objectives/new")]
    [InlineData("/dev/content/encounters/new")]
    public async Task Every_button_waits_while_a_request_is_in_flight_and_every_field_has_a_label(string url)
    {
        using TestApp app = NewApp();

        string body = (await Get(app.NewClient(), url)).Body;

        foreach (Match button in Regex.Matches(body, "<button\\b[^>]*>"))
        {
            Assert.Contains("data-attr:disabled=", button.Value);
        }

        string form = Regex.Match(body, "<form id=\"item-form\".*?</form>", RegexOptions.Singleline).Value;
        foreach (Match field in Regex.Matches(form, "<(input|select)\\b[^>]*>"))
        {
            if (Regex.IsMatch(field.Value, "type=\"hidden\""))
            {
                continue;
            }

            string id = Regex.Match(field.Value, "\\bid=\"([^\"]*)\"").Groups[1].Value;
            Assert.True(Regex.IsMatch(field.Value, "aria-label=\"") || Regex.IsMatch(form, $"<label[^>]*for=\"{Regex.Escape(id)}\""), $"{id} has no label");
        }
    }

    [Fact]
    public async Task A_datastar_row_post_patches_the_main_region_and_a_datastar_save_patches_the_form()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string rows) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", Merge(Objective1, "row=add"), datastar: true);
        (_, string saved) = await Post(client, "/dev/content/encounters/ENCOUNTER_01", Encounter1, datastar: true);

        Assert.Contains("data: elements <main id=\"main\"", rows);
        Assert.Contains("name=\"stages[4].slot\"", rows);
        Assert.Contains("data: elements <main id=\"main\"", saved);
        Assert.Contains("Saved.", saved);
    }

    [Fact]
    public async Task Rows_posts_are_same_origin_only()
    {
        using TestApp app = NewApp();

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01/rows", Encounter1, headers: h => h.Add("Sec-Fetch-Site", "cross-site"));

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task With_developer_mode_off_the_objective_and_encounter_routes_do_not_exist()
    {
        using var app = new TestApp();
        HttpClient client = app.NewClient();

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/dev/content/encounters/ENCOUNTER_01/rows", "row=add")).Status);
    }

    [Theory]
    [InlineData("objectives")]
    [InlineData("encounters")]
    public async Task An_unknown_id_is_404_to_open_save_and_row(string kind)
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        Assert.Equal(HttpStatusCode.NotFound, (await Get(client, $"/dev/content/{kind}/NOT_THERE")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, $"/dev/content/{kind}/NOT_THERE", "name=NAME_PLACEHOLDER")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, $"/dev/content/{kind}/NOT_THERE/rows", "row=add")).Status);
    }

    // --- Edited content is seen in play ---

    [Fact]
    public async Task An_edited_encounter_is_in_the_pack_a_new_legacy_plays_with()
    {
        using TestApp app = NewApp();
        await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", Merge(Encounter1, "name=NAME_PLACEHOLDER_ENCOUNTER"));

        Assert.Equal("NAME_PLACEHOLDER_ENCOUNTER", app.Live.Names.Encounter("ENCOUNTER_01"));
    }

    // --- The repository's own content ---

    [Fact]
    public async Task The_repositorys_own_content_is_byte_identical_after_saves_creates_and_deletes_of_both_kinds()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1", Merge(Objective1, "need=3"));
        await Post(client, "/dev/content/encounters", "id=ENCOUNTER_NEW_1&" + EncounterNone);
        await Post(client, "/dev/content/encounters/ENCOUNTER_NEW_1/delete", "");

        guard.Verify();
    }
}

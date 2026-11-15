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
        + "&approaches[0].id=careful&approaches[0].name=APPROACH_A_NAME&approaches[0].skill=SKILL_A&approaches[0].difficulty=5&approaches[0].gearTag=scanner"
        + "&approaches[1].id=fast&approaches[1].name=APPROACH_B_NAME&approaches[1].skill=SKILL_C&approaches[1].difficulty=6&approaches[1].gearTag="
        + "&hasDecision=true&decision.name=&decision.stakes=1&bold[0].momentum=1&bold[1].strain=1";

    private const string EncounterNone =
        "name=&tags=field&chain="
        + "&approaches[0].id=careful&approaches[0].skill=SKILL_A&approaches[0].difficulty=5&approaches[0].gearTag="
        + "&approaches[1].id=fast&approaches[1].skill=SKILL_B&approaches[1].difficulty=6&approaches[1].gearTag=";

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

    // Every named field a browser would post from the item form: text and hidden inputs, ticked boxes, the chosen option, and text areas.
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

        foreach (Match area in Regex.Matches(form, "<textarea\\b[^>]*\\bname=\"([^\"]*)\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline))
        {
            fields.Add($"{Uri.EscapeDataString(WebUtility.HtmlDecode(area.Groups[1].Value))}={Uri.EscapeDataString(WebUtility.HtmlDecode(area.Groups[2].Value))}");
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
        Assert.Equal(("careful", "APPROACH_A_NAME", "fast"), (Value(body, "approaches[0].id"), Value(body, "approaches[0].name"), Value(body, "approaches[1].id")));
        Assert.Equal("SKILL_D", Selected(body, "approaches[0].skill"));
        Assert.Equal("5", Value(body, "approaches[0].difficulty"));
        Assert.Equal("", Selected(body, "approaches[0].gearTag"));
        Assert.Equal("SKILL_B", Selected(body, "approaches[1].skill"));
        Assert.Equal("6", Value(body, "approaches[1].difficulty"));
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

    // The encounter's words are written inside it, in the file, and open again in the form as they were saved.
    [Fact]
    public async Task An_encounters_words_and_lines_are_saved_inside_it_and_survive_a_second_save()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        string words = "text.arrive=lead: We are at #encounter#.\r\n\r\n  > Signal weak.  \r\n&approaches[0].text.attempt=actor: Slowly now.&approaches[0].text.failure=#actor# slipped.&decision.text.bold=The bold choice held."
            + "&decision.label.bold=Send them in"
            + "&lines[0].beat=injury&lines[0].f0=outcome&lines[0].v0=dead&lines[0].f2=crew.caution&lines[0].v2=1..&lines[0].text=#crew# was run through at #encounter#.";

        (HttpStatusCode status, string saved) = await Post(client, "/dev/content/encounters/ENCOUNTER_01", Merge(Encounter1, words));

        Assert.Equal(HttpStatusCode.OK, status);
        string disk = Disk(app, "encounters/base.yaml");
        // A script is kept one entry to a line, trimmed, with blank lines dropped, and is written as a block so the file reads as it was typed.
        Assert.Contains("  text:\n    arrive: |-\n      lead: We are at #encounter#.\n      > Signal weak.\n  approaches:\n    - id: careful\n      name: APPROACH_A_NAME\n      skill: SKILL_A\n      difficulty: 5\n      gearTag: scanner\n      text:\n        attempt: \"actor: Slowly now.\"\n        failure: \"#actor# slipped.\"\n    - { id: fast, name: APPROACH_B_NAME, skill: SKILL_C, difficulty: 6 }\n", disk, StringComparison.Ordinal);
        Assert.Contains("    text:\n      bold: The bold choice held.\n    labels:\n      bold: Send them in\n  lines:\n    - when: { beat: injury, outcome: dead, crew.caution: { min: 1 } }\n      text: \"#crew# was run through at #encounter#.\"\n", disk, StringComparison.Ordinal);
        Assert.Equal(
            ["ENCOUNTER_01.lines[1]", "ENCOUNTER_01.arrive", "ENCOUNTER_01.careful.attempt", "ENCOUNTER_01.careful.failure", "ENCOUNTER_01.decision.bold"],
            app.Live.Pack.Lines.Where(l => l.Owner == "ENCOUNTER_01").Select(l => l.Id));
        Assert.Equal("Send them in", app.Live.Pack.ChoiceLabels["DECISION_PROMPT_1"]["bold"]);

        (_, string body) = await Get(client, "/dev/content/encounters/ENCOUNTER_01");
        (HttpStatusCode again, _) = await Post(client, "/dev/content/encounters/ENCOUNTER_01", FormFields(body));

        Assert.Equal(HttpStatusCode.OK, again);
        Assert.Equal(disk, Disk(app, "encounters/base.yaml"));
        guard.Verify();
    }

    [Fact]
    public async Task A_line_inside_an_encounter_that_tests_the_encounter_again_is_refused_beside_its_row()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", Merge(Encounter1, "lines[0].beat=injury&lines[0].f0=encounter&lines[0].v0=ENCOUNTER_02&lines[0].text=x"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("The line already holds only for this encounter.", body, StringComparison.Ordinal);
        Assert.Equal(before, Everything(app));
    }

    // --- Approaches ---

    private static string ApproachRow(int row, string id) => $"&approaches[{row}].id={id}&approaches[{row}].skill=SKILL_A&approaches[{row}].difficulty=7&approaches[{row}].gearTag=";

    // A refused save answers as every refused save does (422), with the form drawn again from what was posted.
    private static async Task RefusedWithNothingWritten(TestApp app, string fields, string problem)
    {
        string before = Everything(app);
        string file = Disk(app, "encounters/base.yaml");

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", fields);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("<form id=\"item-form\"", body, StringComparison.Ordinal);
        Assert.Contains(problem, Text(body), StringComparison.Ordinal);
        Assert.Equal(file, Disk(app, "encounters/base.yaml"));
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task Posting_five_approaches_is_refused_with_a_problem()
    {
        using TestApp app = NewApp();

        await RefusedWithNothingWritten(app, Encounter1 + ApproachRow(2, "a") + ApproachRow(3, "b") + ApproachRow(4, "c"), "An encounter may have at most 4 approaches.");
    }

    [Fact]
    public async Task Posting_no_approach_is_refused_with_a_problem()
    {
        using TestApp app = NewApp();

        await RefusedWithNothingWritten(app, Without(Encounter1, "approaches["), "An encounter needs at least one approach.");
    }

    [Fact]
    public async Task Posting_two_approaches_with_one_id_is_refused_with_a_problem()
    {
        using TestApp app = NewApp();

        await RefusedWithNothingWritten(app, Merge(Encounter1, "approaches[1].id=careful"), "Approach id 'careful' is used twice.");
    }

    [Fact]
    public async Task Removing_an_approach_an_own_line_tests_is_refused_on_that_line()
    {
        using TestApp app = NewApp();
        string fields = Without(Encounter1, "approaches[1].") + "&lines[0].beat=check&lines[0].f0=approach&lines[0].v0=fast&lines[0].text=x";

        await RefusedWithNothingWritten(app, fields, "Tests approach 'fast', which this encounter does not have.");
        (_, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", fields);
        Assert.Matches("<input id=\"f-lines-0-v0\"[^>]*aria-invalid=\"true\"", body);
    }

    [Fact]
    public async Task Adding_an_approach_row_redraws_the_form_with_one_more_and_saves_nothing()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01/rows", Merge(Encounter1, "approaches[1].text.attempt=actor: Now.&row=add:approaches"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("fast", Value(body, "approaches[1].id"));
        Assert.Contains(">actor: Now.</textarea>", body, StringComparison.Ordinal);
        Assert.Equal("", Value(body, "approaches[2].id"));
        Assert.Equal("1", Value(body, "approaches[2].difficulty"));
        Assert.Null(Value(body, "approaches[3].id"));
        Assert.Contains("id=\"plays-approach-2\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Saved.", body);
        Assert.Equal(before, Everything(app));
    }

    // At four there is nothing to add, and at one nothing to remove.
    [Fact]
    public async Task Add_is_hidden_at_four_approaches_and_remove_at_one()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string four) = await Post(client, "/dev/content/encounters/ENCOUNTER_01/rows", Encounter1 + ApproachRow(2, "a") + "&row=add:approaches");
        (_, string one) = await Post(client, "/dev/content/encounters/ENCOUNTER_01/rows", Encounter1 + "&row=remove:approaches:1");
        (_, string two) = await Get(client, "/dev/content/encounters/ENCOUNTER_01");

        Assert.NotNull(Value(four, "approaches[3].id"));
        Assert.DoesNotContain("id=\"row-add-approaches\"", four, StringComparison.Ordinal);
        Assert.Contains("id=\"row-remove-approaches-3\"", four, StringComparison.Ordinal);
        Assert.Null(Value(one, "approaches[1].id"));
        Assert.Contains("id=\"row-add-approaches\"", one, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"row-remove-approaches-0\"", one, StringComparison.Ordinal);
        Assert.Contains("id=\"row-remove-approaches-1\"", two, StringComparison.Ordinal);
    }

    // --- What plays here ---

    // The panel lists the encounter's beats in mission order; a blank box shows the line that plays there now, and a beat with no box shows it with the way to write one.
    [Fact]
    public async Task The_panel_shows_what_plays_at_each_beat_and_the_fallback_in_a_blank_box()
    {
        using TestApp app = NewApp();

        string body = Text((await Get(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01")).Body);

        Assert.Contains("<section class=\"plays\" id=\"plays\"", body, StringComparison.Ordinal);
        int[] order = new[] { "plays-arrive", "plays-approach-0", "plays-approach-1", "plays-checks", "plays-effects", "plays-injury", "plays-lead", "plays-decision", "plays-answer", "plays-skip", "plays-chain", "plays-collapse" }
            .Select(id => body.IndexOf($"id=\"{id}\"", StringComparison.Ordinal)).ToArray();
        Assert.All(order, at => Assert.True(at > 0));
        Assert.Equal(order.Order(), order);
        Assert.Matches("<textarea id=\"f-text-arrive\" name=\"text.arrive\"[^>]*placeholder=\"The team reached #encounter#.\"", body);
        Assert.Matches("(?s)<div class=\"plays__preview\" id=\"pv-f-text-arrive\">.*?The team reached ENCOUNTER_01\\..*?Shared line LINE_STAGE\\.", body);
        Assert.Matches("(?s)id=\"pv-any-injury-injury\">.*?was lightly injured at ENCOUNTER_01\\..*?Shared line LINE_INJURY_LIGHT\\.", body);
        Assert.Contains("id=\"row-write-injury-injury\" name=\"row\" value=\"write:injury\"", body, StringComparison.Ordinal);
        Assert.Contains("name=\"sample\" value=\"0\"", body, StringComparison.Ordinal);
        Assert.Contains("Sample 1: CREW_01 leads, CREW_02 acts.", body, StringComparison.Ordinal);
        Assert.Contains("data-insert=\"lead: \"", body, StringComparison.Ordinal);
        Assert.Contains("data-insert=\"#encounter#\"", body, StringComparison.Ordinal);
    }

    // Typing asks for the previews only: each is patched by its id, no box is sent back, and nothing is saved.
    [Fact]
    public async Task The_preview_route_shows_the_forms_unsaved_words_and_touches_no_box()
    {
        using TestApp app = NewApp();
        string before = Everything(app);
        string words = "text.arrive=lead: We are at #encounter#.\n> Signal weak.&approaches[0].text.failure=#nobody# slipped.&approaches[0].text.disaster=-"
            + "&lines[0].beat=injury&lines[0].f0=outcome&lines[0].v0=dead&lines[0].text=#crew# was run through at #encounter#."
            + "&lines[1].beat=check&lines[1].f0=skill&lines[1].v0=SKILL_D&lines[1].text=Never.";

        (HttpStatusCode status, string raw) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01/plays?sample=1", Merge(Encounter1, words), datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        string body = Text(raw);
        Assert.DoesNotContain("<textarea", body, StringComparison.Ordinal);
        Assert.DoesNotContain("mode replace", body, StringComparison.Ordinal);
        Assert.Contains("name=\"sample\" value=\"1\"", body, StringComparison.Ordinal);
        Assert.Contains("Sample 2: CREW_02 leads, CREW_03 acts.", body, StringComparison.Ordinal);
        Assert.Contains("?sample=2'", body, StringComparison.Ordinal);
        Assert.Matches("(?s)id=\"pv-f-text-arrive\">.*?<span class=\"script__who\">CREW_02</span>.*?We are at ENCOUNTER_01\\..*?script__line--note.*?Signal weak\\..*?Its own words\\.", body);
        Assert.Matches("(?s)id=\"pv-f-approaches-0-text-failure\">.*?<p class=\"error\">#nobody# names nothing", body);
        Assert.Matches("(?s)id=\"pv-f-approaches-0-text-disaster\">.*?Nothing is said\\..*?Its own words\\.", body);
        // A line with criteria is shown against a sample made to satisfy them, and one that can never hold here says so.
        Assert.Matches("(?s)id=\"pv-line-0\">.*?CREW_03 was run through at ENCOUNTER_01\\..*?This line wins in this sample\\.", body);
        Assert.Matches("(?s)id=\"pv-line-1\">.*?This cannot happen here: the checks at this encounter are on SKILL_A and SKILL_C\\.", body);
        Assert.Equal(before, Everything(app));
    }

    // "Write one for here" starts a line of the encounter's own from what plays at that beat now; its fixed criterion is shown, locked.
    [Fact]
    public async Task Write_one_for_here_starts_a_line_from_the_shared_one_with_the_encounter_shown_as_fixed()
    {
        using TestApp app = NewApp();
        string before = Everything(app);

        (HttpStatusCode status, string raw) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01/rows", Merge(Encounter1, "row=write:injury"), datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        string body = Text(raw);
        Assert.Contains("<input type=\"hidden\" name=\"lines[0].beat\" value=\"injury\" />", body, StringComparison.Ordinal);
        Assert.Matches("<textarea id=\"f-lines-0-text\" name=\"lines\\[0\\].text\"[^>]*data-init=\"el.focus\\(\\);[^>]*>#crew# was lightly injured at #encounter#.</textarea>", body);
        Assert.Matches("(?s)<tr class=\"criteria__locked\">.*?value=\"encounter\" disabled.*?value=\"ENCOUNTER_01\" disabled.*?Always, because the line is written here\\.", body);
        Assert.Contains("name=\"lines[0].f0\"", body, StringComparison.Ordinal);
        Assert.Contains("list=\"facts-injury\"", body, StringComparison.Ordinal);
        Assert.Contains("id=\"row-remove-lines-0\"", body, StringComparison.Ordinal);
        Assert.Equal(before, Everything(app));
    }

    [Fact]
    public async Task The_lists_say_how_much_of_each_item_is_written()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        await Post(client, "/dev/content/encounters/ENCOUNTER_01", Merge(Encounter1, "text.arrive=Here.&lines[0].beat=injury&lines[0].text=Hurt."));

        string encounters = Text((await Get(client, "/dev/content/encounters")).Body);
        string objectives = Text((await Get(client, "/dev/content/objectives")).Body);

        Assert.Contains("<th scope=\"col\">Words written</th>", encounters, StringComparison.Ordinal);
        Assert.Contains(">1 of 17, 1 line</td>", encounters, StringComparison.Ordinal);
        Assert.Contains(">0 of 13</td>", encounters, StringComparison.Ordinal);
        Assert.Contains(">0 of 5</td>", objectives, StringComparison.Ordinal);
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

        (HttpStatusCode status, _) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", Merge(Encounter1, "name=NAME_PLACEHOLDER&chain=&approaches[0].difficulty=4&approaches[0].gearTag=tools&decision.stakes=3&decision.name=NAME_PLACEHOLDER_2"));

        Assert.Equal(HttpStatusCode.OK, status);
        EncounterDef live = app.Live.Pack.Encounters.Single(e => e.Id == "ENCOUNTER_01");
        Assert.Equal("NAME_PLACEHOLDER", live.Name);
        Assert.Null(live.Chain);
        Assert.Equal(4, live.Approaches[0].Difficulty);
        Assert.Equal("tools", live.Approaches[0].GearTag);
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

    // A legacy whose site offers the template keeps playing without it, and the confirmation says so before the delete.
    [Fact]
    public async Task The_objective_delete_confirmation_says_legacies_will_skip_it()
    {
        using TestApp app = NewApp();

        (_, string objective) = await Get(app.NewClient(), "/dev/content/objectives/OBJECTIVE_TEMPLATE_1");
        (_, string encounter) = await Get(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01");

        Assert.Contains("Legacies whose current site offers it will skip it.", objective);
        Assert.DoesNotContain("Legacies whose current site offers it will skip it.", encounter);
    }

    // At 400 wide a select shows about 14 characters: a long name and id put the id first, so the id stays in view.
    [Fact]
    public async Task A_long_name_and_id_put_the_id_first_in_a_select_and_a_short_pair_keeps_the_name_first()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        await Post(client, "/dev/content/skills/SKILL_A", "name=NAME_PLACEHOLDER_SKILL");
        await Post(client, "/dev/content/skills/SKILL_C", "name=AB");
        await Post(client, "/dev/content/encounters/ENCOUNTER_CHAIN", Merge(EncounterNone, "name=NAME_PLACEHOLDER_CHAIN&tags=chained"));

        (_, string body) = await Get(client, "/dev/content/encounters/ENCOUNTER_01");

        Assert.Contains(">SKILL_A (NAME_PLACEHOLDER_SKILL)</option>", body);
        Assert.Contains(">AB (SKILL_C)</option>", body);
        Assert.Contains(">ENCOUNTER_CHAIN (NAME_PLACEHOLDER_CHAIN)</option>", body);
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
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "approaches[0].skill=", "Choose a skill." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "approaches[1].skill=SKILL_NOT_THERE", "Unknown skill." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "approaches[0].difficulty=0", "Must be 1 or more." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "approaches[1].difficulty=", "Enter a whole number." },
        { "/dev/content/encounters/ENCOUNTER_01", Encounter1, "approaches[0].gearTag=not_a_tag", "Unknown gear tag." },
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

        (_, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", Merge(Encounter1, "approaches[0].difficulty=0&bold[1].strain=7&tags=typed_tag"));

        Assert.Equal("typed_tag", Value(body, "tags"));
        Assert.Equal("7", Value(body, "bold[1].strain"));
        Assert.Equal("0", Value(body, "approaches[0].difficulty"));
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

    // Ticking a draw tag posts the form to the rows route, keeping focus on the box, so the count is always current.
    [Fact]
    public async Task A_draw_tag_box_redraws_the_match_count_and_keeps_its_focus()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string body) = await Get(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1");
        string box = Regex.Match(body, "<input[^>]*id=\"f-stages-0-draw-1\"[^>]*>").Value;
        (_, string rows) = await Post(client, "/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows?focus=f-stages-0-draw-1", Merge(Objective1, "stages[0].draw=field&stages[0].draw=hazard"), datastar: true);

        Assert.DoesNotContain("Counted when the form was last refreshed.", body);
        Assert.Contains("data-on:change=\"@post(&#x27;/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows?focus=f-stages-0-draw-1&#x27;, {contentType: &#x27;form&#x27;})\"", box);
        Assert.Contains("data-indicator:_rowing", box);
        Assert.Contains("id=\"stage-0-match\" role=\"status\">3 encounters match</p>", rows);
        Assert.Equal(["f-stages-0-draw-1"], Focused(rows));
    }

    [Fact]
    public async Task One_match_says_encounter_not_encounters()
    {
        using TestApp app = NewApp(root =>
        {
            string file = Path.Combine(root, "encounters", "base.yaml");
            File.WriteAllText(file, File.ReadAllText(file).Replace("tags: [field, technical]\n  chain: ENCOUNTER_CHAIN\n  approaches:\n    - { id: careful, name: APPROACH_A_NAME, skill: SKILL_B, difficulty: 5, gearTag: tools }", "tags: [field, technical, rare]\n  chain: ENCOUNTER_CHAIN\n  approaches:\n    - { id: careful, name: APPROACH_A_NAME, skill: SKILL_B, difficulty: 5, gearTag: tools }"));
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

    // After a row action every control takes its state from this answer: the region is replaced, not morphed, so no box the
    // author ticked is kept at its old place on the page.
    [Theory]
    [InlineData("/dev/content/encounters/ENCOUNTER_01/rows", "row=remove:bold:0")]
    [InlineData("/dev/content/encounters/ENCOUNTER_01/rows?focus=f-hasDecision", "hasDecision=")]
    [InlineData("/dev/content/objectives/OBJECTIVE_TEMPLATE_1/rows", "row=down:0")]
    [InlineData("/dev/content/objectives/rows", "row=add")]
    [InlineData("/dev/content/assignments/ASSIGNMENT_TEMPLATE_1/rows", "row=remove:0")]
    public async Task A_rows_answer_replaces_the_main_region_instead_of_morphing_it(string url, string action)
    {
        using TestApp app = NewApp();
        string fields = url.Contains("encounters", StringComparison.Ordinal) ? Merge(Encounter1, action) : url.Contains("objectives", StringComparison.Ordinal) ? Merge(Objective1, action) : action + "&offer[0].count=1&offer[0].weight=1";

        (_, string body) = await Post(app.NewClient(), url, fields, datastar: true);

        Assert.Contains("data: mode replace\n", body);
        Assert.Contains("data: elements <main id=\"main\">", body);
    }

    // A save answers by morphing as before: nothing on the page has moved.
    [Fact]
    public async Task A_save_answer_still_morphs()
    {
        using TestApp app = NewApp();

        (_, string body) = await Post(app.NewClient(), "/dev/content/encounters/ENCOUNTER_01", Encounter1, datastar: true);

        Assert.DoesNotContain("data: mode", body);
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

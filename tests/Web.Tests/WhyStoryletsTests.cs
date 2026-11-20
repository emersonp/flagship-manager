using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Traits;

namespace Web.Tests;

// Dev Play's "Why these storylets" (1e part four, Task 7, design 4.10), through the real app on a scratch copy of the fixture, where
// one storylet arrives a cycle and STORYLET_01 has a global cooldown of 3. The first cycle sends no one, so every crew member rested.
// Then, as missions would: trait_h from CREW_02 toward CREW_04 brings STORYLET_04 (stakes 3, two clauses: 32); trait_a on the ship
// brings STORYLET_01 (stakes 2, one clause: 21), which loses on the budget; trait_g at 5 from CREW_01 toward CREW_03 brings
// VIGNETTE_04 (two clauses) but is past STORYLET_03's 3 to 4; and VIGNETTE_01 comes to someone who rested (one clause).
public sealed class WhyStoryletsTests : IDisposable
{
    private readonly TestApp app = new(developerMode: true, scratch: true, prepare: root =>
    {
        Replace(Path.Combine(root, "tuning", "missions.yaml"), "storylets:\n  perCycle: 2", "storylets:\n  perCycle: 1");
        Replace(Path.Combine(root, "storylets", "base.yaml"), "- id: STORYLET_01\n  stakes: 2\n", "- id: STORYLET_01\n  stakes: 2\n  cooldown: { global: 3 }\n");
    });

    private readonly HttpClient client;

    public WhyStoryletsTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private static void Replace(string path, string from, string to)
    {
        string text = File.ReadAllText(path);
        Assert.Contains(from, text);
        File.WriteAllText(path, text.Replace(from, to, StringComparison.Ordinal));
    }

    private static StoredTrait Trait(string trait, string? crewId, string? targetId, int value, int day)
    {
        return new StoredTrait { Trait = trait, CrewId = crewId, TargetId = targetId, Value = value, ChangedAtDay = day, ChangedAtCycle = 1, EncounterId = "ENCOUNTER_01" };
    }

    // The first cycle, sending no one, to its aftermath; then the traits above.
    private async Task<Guid> ToAftermath()
    {
        Guid id = await app.CreateAtOrders("LEGACY", 1);
        await Run(id, legacy =>
        {
            To(legacy, CyclePhase.Aftermath);
            List<StoredTrait> traits = legacy.State.Module<TraitsState>(TraitsModule.Name).Traits;
            traits.Add(Trait("trait_h", "CREW_02", "CREW_04", 1, legacy.State.Day));
            traits.Add(Trait("trait_a", null, null, 1, legacy.State.Day));
            traits.Add(Trait("trait_g", "CREW_01", "CREW_03", 5, legacy.State.Day));
        });
        return id;
    }

    private Task Run(Guid id, Action<LoadedLegacy> act)
    {
        return app.Sessions.Run(app.Live, id, legacy =>
        {
            act(legacy);
            return legacy;
        });
    }

    private static void To(LoadedLegacy legacy, CyclePhase phase)
    {
        do
        {
            legacy.Send(new AdvancePhase());
        }
        while (legacy.State.Phase != phase);
    }

    private async Task<string> Why(Guid id)
    {
        string page = WebUtility.HtmlDecode(await client.GetStringAsync($"/dev/legacy/{id}"));
        return Section(page);
    }

    private static string Section(string html)
    {
        string section = Regex.Match(html, "<section id=\"why\".*?</section>", RegexOptions.Singleline).Value;
        Assert.NotEqual("", section);
        return section;
    }

    // A search, answered with the section patched: what it says, as text.
    private async Task<string> Search(Guid id, string query)
    {
        HttpResponseMessage response = await client.PostAsync($"/dev/legacy/{id}/why", new FormUrlEncodedContent([new("q", query)]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Tags out before the text is decoded, so a decoded "<=" is not read as one.
        string found = Regex.Match(Section(await response.Content.ReadAsStringAsync()), "<div id=\"why-found\".*?</div>", RegexOptions.Singleline).Value;
        return WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(found, "<[^>]+>", " "), "\\s+", " ")).Trim();
    }

    // Each contender's row: id, cast, stakes, specificity, freshness, salience, and fate, as text, in the order shown.
    private static List<string> Rows(string section, string table)
    {
        string body = Regex.Match(section, $"<table[^>]*id=\"{table}\".*?</table>", RegexOptions.Singleline).Value;
        return Regex.Matches(body, "<tr data-id=.*?</tr>", RegexOptions.Singleline)
            .Select(m => string.Join(" | ", Regex.Matches(m.Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline).Select(c => Regex.Replace(c.Groups[1].Value, "<[^>]+>", "").Trim())))
            .ToList();
    }

    [Fact]
    public async Task The_count_line_counts_eligible_cast_and_arrived()
    {
        Guid id = await ToAftermath();

        string next = await Why(id);
        await Run(id, legacy => To(legacy, CyclePhase.Itinerary));
        string open = await Why(id);

        // Eligible: STORYLET_01, 03, and 04, and VIGNETTE_01 and 04; STORYLET_02's, STORYLET_05's, STORYLET_06's (the chronicle holds no deaths), VIGNETTE_02's, and VIGNETTE_03's
        // first slot (or the ship) held for no one. STORYLET_03 is eligible and does not cast; STORYLET_01 casts and loses on the budget.
        Assert.Contains("6 storylets, 4 vignettes: 5 eligible, 4 cast, 3 arrived", next);
        Assert.Contains("The next itinerary, cycle 2, as things stand now", next);
        Assert.Contains("6 storylets, 4 vignettes: 5 eligible, 4 cast, 3 arrived", open);
        Assert.Contains("The itinerary now open, cycle 2, as it opened", open);
    }

    [Fact]
    public async Task Contenders_are_in_salience_order_with_their_parts()
    {
        Guid id = await ToAftermath();
        await Run(id, legacy => To(legacy, CyclePhase.Itinerary));

        string why = await Why(id);
        // Answering clears trait_h, which STORYLET_04 read; the itinerary is still explained as it opened.
        await Run(id, legacy => legacy.Send(new AnswerStorylet("2:STORYLET_04", "option_c")));
        string answered = await Why(id);

        List<string> storylets = Rows(why, "why-storylets");
        Assert.Equal(2, storylets.Count);
        Assert.Matches("^STORYLET_04 \\| CREW_02 · CREW_04 \\| 30 \\| 2 \\| 0 \\| [12] \\| 32 \\| Arrived \\| Edit$", storylets[0]);
        Assert.Matches("^STORYLET_01 \\| The ship \\| 20 \\| 1 \\| 0 \\| [12] \\| 21 \\| Lost on the budget \\| Edit$", storylets[1]);
        List<string> vignettes = Rows(why, "why-vignettes");
        Assert.Equal(2, vignettes.Count);
        Assert.Matches("^VIGNETTE_04 \\| CREW_01 · CREW_03 \\| 0 \\| 2 \\| 0 \\| [12] \\| 2 \\| Arrived \\| Edit$", vignettes[0]);
        Assert.Matches("^VIGNETTE_01 \\| CREW_0\\d \\| 0 \\| 1 \\| 0 \\| [12] \\| 1 \\| Arrived \\| Edit$", vignettes[1]);
        Assert.Contains("href=\"/dev/content/files/storylets\"", why);
        Assert.Contains("href=\"/dev/content/files/vignettes\"", why);
        Assert.Equal(storylets, Rows(answered, "why-storylets"));
        Assert.Equal(vignettes, Rows(answered, "why-vignettes"));
    }

    [Fact]
    public async Task Search_names_the_failing_clause_and_slot()
    {
        Guid id = await ToAftermath();

        string two = await Search(id, "STORYLET_02");
        string three = await Search(id, "storylet_03");
        string four = await Search(id, "STORYLET_04");
        string none = await Search(id, "NOSUCH");

        Assert.Contains("STORYLET_02: subject: subject.trait.trait_b >= 1 held for no one.", two);
        Assert.Contains("STORYLET_03: target: subject.trait.trait_g.toward(target) <= 4 held for no one; the clauses before it held for 1.", three);
        Assert.Contains("STORYLET_04: arrives, with salience 32.", four);
        Assert.Contains("No storylet or vignette has an id or name with NOSUCH in it.", none);
    }

    [Fact]
    public async Task Search_names_the_cooldown_and_when_it_ends()
    {
        Guid id = await ToAftermath();
        await Run(id, legacy =>
        {
            // Cycle 2: STORYLET_04 and the vignettes arrive; STORYLET_01 loses on the budget, so trait_h is cleared and it arrives in
            // cycle 3; then trait_a is raised again for cycle 4.
            To(legacy, CyclePhase.Itinerary);
            legacy.Send(new AnswerStorylet("2:STORYLET_04", "option_c"));
            To(legacy, CyclePhase.Itinerary);
            legacy.Send(new AnswerStorylet("3:STORYLET_01", "option_a"));
            To(legacy, CyclePhase.Aftermath);
            legacy.State.Module<TraitsState>(TraitsModule.Name).Traits.Add(Trait("trait_a", null, null, 1, legacy.State.Day));
        });

        string one = await Search(id, "STORYLET_01");
        string four = await Search(id, "VIGNETTE_04");

        Assert.Contains("STORYLET_01: in its global cooldown of 3 cycles; it may arrive again from cycle 6.", one);
        Assert.Contains("VIGNETTE_04: every cast that held is in its cast cooldown of 6 cycles; CREW_01 · CREW_03 may have it again from cycle 8.", four);
    }
}

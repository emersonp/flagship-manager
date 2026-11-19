using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Traits;

namespace Web.Tests;

// A pair on the itinerary (1e part three, Task 3), through the real app on a scratch copy of the fixture: trait_g at 3, held by CREW_01
// toward CREW_03, brings STORYLET_03 and VIGNETTE_04 for that pair, and each panel's head names both (from part four's Task 7, by name
// alone). The fixture's other vignettes are left out, since the rested crew's would fill the cycle.
public sealed class PairBridgeTests : IDisposable
{
    private readonly TestApp app = new(scratch: true, prepare: root =>
    {
        string path = Path.Combine(root, "vignettes", "base.yaml");
        string text = File.ReadAllText(path);
        File.WriteAllText(path, text[text.IndexOf("- id: VIGNETTE_04", StringComparison.Ordinal)..]);
    });

    public void Dispose()
    {
        app.Dispose();
    }

    // Task 7 (1e part four, design 4.9): the head lists every cast by name in slot order, storylets and vignettes alike, and a ship
    // storylet lists none. Part three's pair head ("CREW_01 · trait_g CREW_03") is gone. trait_a on the ship brings STORYLET_01 too.
    [Fact]
    public async Task The_head_lists_the_cast()
    {
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY", 1);
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != CyclePhase.Aftermath)
            {
                legacy.Send(new AdvancePhase());
            }

            List<StoredTrait> traits = legacy.State.Module<TraitsState>(TraitsModule.Name).Traits;
            traits.Add(new StoredTrait { Trait = "trait_g", CrewId = "CREW_01", TargetId = "CREW_03", Value = 3, ChangedAtWeek = legacy.State.Week, EncounterId = "ENCOUNTER_05" });
            traits.Add(new StoredTrait { Trait = "trait_a", CrewId = null, TargetId = null, Value = 1, ChangedAtWeek = legacy.State.Week, EncounterId = "ENCOUNTER_01" });
            return legacy;
        });
        HttpResponseMessage advanced = await client.PostAsync($"/legacy/{id}/advance", new FormUrlEncodedContent(app.Stamped(id, [])));
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        string page = WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));

        string Article(string line)
        {
            return Regex.Matches(page, "<article class=\"storylet[^\"]*\".*?</article>", RegexOptions.Singleline).Select(m => m.Value).Single(a => a.Contains(line, StringComparison.Ordinal));
        }

        string pair = Article("STORYLET_03_LINE_2");
        Assert.Contains("<p class=\"note\">CREW_01 · CREW_03</p>", pair);
        Assert.Contains("From ENCOUNTER_05 · trait_g", pair);
        Assert.Contains("<p class=\"note\">CREW_01 · CREW_03</p>", Article("VIGNETTE_04_LINE_2"));
        Assert.DoesNotContain("trait_g", Regex.Match(pair, "<div class=\"storylet__head\">.*?</div>", RegexOptions.Singleline).Value.Replace("From ENCOUNTER_05 · trait_g", "", StringComparison.Ordinal));
        Assert.Equal(["From ENCOUNTER_01 · trait_a"], Regex.Matches(Article("STORYLET_01_LINE_1"), "<p class=\"note\">(.*?)</p>").Select(m => m.Groups[1].Value));
    }
}

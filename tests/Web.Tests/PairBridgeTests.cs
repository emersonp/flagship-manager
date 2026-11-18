using System.Net;
using Sim.Core;
using Sim.Traits;

namespace Web.Tests;

// A pair on the itinerary (1e part three, Task 3), through the real app on a scratch copy of the fixture: trait_g at 3, held by CREW_01
// toward CREW_03, brings STORYLET_03 and VIGNETTE_04 for that pair, and each panel's head names both. The fixture's other vignettes are
// left out, since the rested crew's would fill the cycle.
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

    [Fact]
    public async Task A_pairs_storylet_and_vignette_name_the_holder_the_trait_and_the_target()
    {
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY", 1);
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != CyclePhase.Aftermath)
            {
                legacy.Send(new AdvancePhase());
            }

            legacy.State.Module<TraitsState>(TraitsModule.Name).Traits.Add(new StoredTrait { Trait = "trait_g", CrewId = "CREW_01", TargetId = "CREW_03", Value = 3, ChangedAtWeek = legacy.State.Week, EncounterId = "ENCOUNTER_05" });
            return legacy;
        });
        HttpResponseMessage advanced = await client.PostAsync($"/legacy/{id}/advance", new FormUrlEncodedContent(app.Stamped(id, [])));
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);

        string page = WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));

        Assert.Contains("<p class=\"note\">CREW_01 · trait_g CREW_03</p>", page);
        Assert.Contains("From ENCOUNTER_05 · trait_g", page);
        Assert.Contains("CREW_01 · trait_g CREW_03</p>", page[page.IndexOf("VIGNETTE_04", StringComparison.Ordinal)..]);
        Assert.Contains("STORYLET_03_LINE_2", page);
        Assert.Contains("VIGNETTE_04_LINE_2", page);
    }
}

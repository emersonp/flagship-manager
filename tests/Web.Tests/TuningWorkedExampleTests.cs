using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Missions;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// Part one's worked example through the real app (1f part one, Task 14; design 4.10 and 4.11), on a scratch copy of the fixture so it
// does not depend on the game's numbers: open Tuning from "Bench this" on an objective in Dev Play's Orders, tick the legacy's crew,
// change one number in the draft, run, see saved, draft, and change, save, and launch the legacy's next mission from the briefing as
// the browser does: its starting state carries the new number.
public sealed class TuningWorkedExampleTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private const string File = "tuning/missions.yaml";

    [Fact]
    public async Task Bench_this_change_a_number_run_save_and_the_next_mission_uses_it()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_TUNED", 1);
        string before = Disk(app, File);
        Assert.Equal(2, app.Live.Pack.Tuning.StrainFailure);

        // "Bench this" on the site's first objective, in Dev Play's Orders.
        (_, string orders) = await Get(client, $"/dev/legacy/{id}");
        string bench = WebUtility.HtmlDecode(Regex.Match(orders, "<a class=\"dev-edit\" href=\"([^\"]+)\"[^>]*>Bench this</a>").Groups[1].Value);
        string template = Regex.Match(bench, "objective=([A-Z0-9_]+)").Groups[1].Value;
        Assert.StartsWith($"/dev/tuning?objective={template}&team=legacy&source={id}", bench);
        (HttpStatusCode status, string page) = await Get(client, bench);
        Assert.Equal(HttpStatusCode.OK, status);

        // Tick the first two members the launch would take; the first ticked leads.
        List<string> crew = Regex.Matches(page, "<input type=\"checkbox\" name=\"crew\" value=\"([A-Z0-9_]+)\"(?! disabled)").Select(m => m.Groups[1].Value).Take(2).ToList();
        Assert.Equal(2, crew.Count);
        string inputs = $"{bench[(bench.IndexOf('?') + 1)..]}&crew={crew[0]}&crew={crew[1]}&lead={crew[0]}&runs=500";

        // One number changed in the draft: a failure adds one strain, not two.
        (status, string draft) = await Post(client, "/dev/tuning/draft?path=compounding.strain.failure", "compounding.strain.failure=1", datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("saved <span class=\"m\">2</span>", draft);
        Assert.Equal(before, Disk(app, File));

        // Run: the legacy's team, built once, under the saved tuning and the draft on the same seeds, and the change between them.
        (status, string run) = await Post(client, "/dev/tuning/run?page=p1", inputs, datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        int end = run.IndexOf("<span class=\"m\">500</span> of <span class=\"m\">500</span> runs in each column, from seed <span class=\"m\">1</span>", StringComparison.Ordinal);
        Assert.True(end > 0, "the run did not finish");
        string figures = run[end..];
        Assert.Contains(">Saved</th>", figures);
        Assert.Contains(">Draft</th>", figures);
        Assert.Contains(">Change</th>", figures);
        Assert.Equal(before, Disk(app, File));

        // Save: the one number changes in the file, and the live pack has it.
        (status, string saved) = await Post(client, "/dev/tuning/save", "", datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<span class=\"m\">compounding.strain.failure</span> is now <span class=\"m\">1</span>", saved);
        Assert.Equal(before.Replace("    failure: 2\n", "    failure: 1\n", StringComparison.Ordinal), Disk(app, File));
        Assert.Equal(1, app.Live.Pack.Tuning.StrainFailure);

        // The legacy's next mission, launched from the briefing as the browser posts it, starts on the new number.
        await Bridge(app, client, id, "advance", []);
        Assert.Equal(CyclePhase.Briefing, (await app.Sessions.Read(app.Live, id))!.State.Phase);
        MissionsState missions = (await app.Sessions.Read(app.Live, id))!.State.Module<MissionsState>(MissionsModule.Name);
        string objective = missions.Site!.Objectives.First(o => o.TemplateId == template).Id;
        await Bridge(app, client, id, "launch", [new("objective", objective), new("crew", crew[0]), new("crew", crew[1]), new("lead", crew[0]), new("priority", "0"), new("autonomy", "0")]);

        MissionInFlight? mission = (await app.Sessions.Read(app.Live, id))!.State.Module<MissionsState>(MissionsModule.Name).Site?.Mission;
        Assert.NotNull(mission);
        Assert.Equal(crew, mission.State.Team.Select(m => m.Member.Id));
        Assert.Equal(1, mission.State.Tuning.StrainFailure);
        guard.Verify();
    }

    private static async Task Bridge(TestApp app, HttpClient client, Guid id, string action, List<KeyValuePair<string, string>> form)
    {
        HttpResponseMessage response = await client.PostAsync($"/legacy/{id}/{action}", new FormUrlEncodedContent(app.Stamped(id, form)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await response.Content.ReadAsStringAsync();
    }
}

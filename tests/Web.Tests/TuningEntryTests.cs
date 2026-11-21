using System.Net;
using System.Text.RegularExpressions;
using Sim.Core;
using Sim.Missions;
using Web.Bridge;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// The Tuning page's entry points (1f part one, Task 13; design 4.6): the editor index, legacy selection, Dev Play's panel, and "Bench
// this" on each objective in Orders and the briefing and on the mission just finished in the aftermath. Each is drawn in developer
// mode only, and lands on the page with the inputs it names.
public sealed class TuningEntryTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private const string Objective = "OBJECTIVE_TEMPLATE_1";

    // Every "Bench this" link on a page, its address unescaped as the browser follows it.
    private static List<string> BenchLinks(string body)
    {
        return Regex.Matches(body, "<a class=\"dev-edit\" href=\"([^\"]+)\"[^>]*>Bench this</a>").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
    }

    private static async Task Until(TestApp app, Guid id, Func<LegacyState, bool> done)
    {
        for (int steps = 0; !done((await app.Sessions.Read(app.Live, id))!.State); steps++)
        {
            Assert.True(steps < 400, "The legacy never got there.");
            await app.Sessions.Run(app.Live, id, legacy =>
            {
                legacy.Send(SimRunner.AutoCaptain.NextCommand(app.Live.Game, legacy.State, app.Live.Pack));
                return legacy;
            });
        }
    }

    // The page as it lands from a link: the objective chosen, this legacy as the source, and each member named ticked.
    private static void LandsOn(string page, string objective, Guid id, IReadOnlyList<string> crew, string? lead)
    {
        Assert.Contains($"<option value=\"{objective}\" selected", page);
        Assert.Contains($"<option value=\"{id}\" selected", page);
        foreach (string member in crew)
        {
            Assert.Matches($"<input type=\"checkbox\" name=\"crew\" value=\"{member}\" checked", page);
        }

        Assert.DoesNotMatch($"<input type=\"checkbox\" name=\"crew\" value=\"(?!{string.Join("|", crew.DefaultIfEmpty("NOBODY"))}\")[A-Z0-9_]+\" checked", page);
        if (lead is not null)
        {
            Assert.Matches($"<select id=\"bi-lead\" name=\"lead\">.*?<option value=\"{lead}\" selected", Regex.Replace(page, "\\s+", " "));
        }
    }

    [Fact]
    public async Task The_editor_index_links_mission_tuning_to_the_page_and_keeps_its_text_view()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();

        (_, string index) = await Get(client, "/dev/content");
        (_, string missions) = await Get(client, "/dev/content/files/missions");
        (_, string crew) = await Get(client, "/dev/content/files/crew-tuning");
        (HttpStatusCode status, _) = await Get(client, "/dev/tuning");

        Assert.Contains("<a href=\"/dev/tuning\">Mission tuning</a>", index);
        Assert.Contains("<a href=\"/dev/content/files/missions\">tuning/missions.yaml</a>", index);
        Assert.Contains("Edit these numbers and re-run the bench on <a href=\"/dev/tuning\">the Tuning page</a>.", missions);
        Assert.DoesNotContain("comes later", WebUtility.HtmlDecode(missions));
        Assert.Contains("in its Legacy mode (1f part two); edit them as text until then.", WebUtility.HtmlDecode(crew));
        Assert.DoesNotContain("/dev/tuning", crew);
        Assert.Equal(HttpStatusCode.OK, status);

        // Outside developer mode there is no editor, and no page to link to.
        using var hosted = new TestApp();
        Assert.Equal(HttpStatusCode.NotFound, (await Get(hosted.NewClient(), "/dev/content")).Status);
        guard.Verify();
    }

    [Fact]
    public async Task Legacy_selection_and_dev_plays_panel_link_the_page_in_developer_mode_only()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ENTRY", 1);

        (_, string list) = await Get(client, "/");
        (_, string dev) = await Get(client, $"/dev/legacy/{id}");
        (_, string play) = await Get(client, $"/legacy/{id}");

        Assert.Contains("<a class=\"home\" href=\"/dev/content\">Content</a> <a class=\"home\" href=\"/dev/tuning\">Tuning</a>", list);
        string tuning = EditorLinks.Tuning(id);
        Assert.Equal($"/dev/tuning?team=legacy&source={id}", tuning);
        Assert.Contains($"<a class=\"home\" href=\"{tuning.Replace("&", "&amp;")}\" target=\"_blank\" rel=\"noopener\">Tuning</a>", dev);
        Assert.DoesNotContain("/dev/tuning", play);

        // Dev Play's link lands with this legacy as the team source and nobody chosen.
        (HttpStatusCode status, string page) = await Get(client, tuning);
        Assert.Equal(HttpStatusCode.OK, status);
        LandsOn(page, Objective, id, [], null);

        using var hosted = new TestApp();
        await hosted.CreateAtOrders("LEGACY_HOSTED", 1);
        (_, string hostedList) = await Get(hosted.NewClient(), "/");
        Assert.DoesNotContain("/dev/tuning", hostedList);
        guard.Verify();
    }

    [Fact]
    public async Task Bench_this_in_orders_names_each_objective_and_the_legacy_with_nobody_chosen()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ENTRY", 1);
        Assert.Equal(CyclePhase.Orders, (await app.Sessions.Read(app.Live, id))!.State.Phase);
        List<string> objectives = (await app.Sessions.Read(app.Live, id))!.State.Module<MissionsState>(MissionsModule.Name).Site!.Objectives.Select(o => o.TemplateId).ToList();

        (_, string dev) = await Get(client, $"/dev/legacy/{id}");
        (_, string play) = await Get(client, $"/legacy/{id}");

        Assert.Equal(objectives.Select(o => $"/dev/tuning?objective={o}&team=legacy&source={id}"), BenchLinks(dev));
        Assert.DoesNotContain("Bench this", play);
        Assert.DoesNotContain("/dev/tuning", play);

        (HttpStatusCode status, string page) = await Get(client, BenchLinks(dev)[^1]);
        Assert.Equal(HttpStatusCode.OK, status);
        LandsOn(page, objectives[^1], id, [], null);
        guard.Verify();
    }

    [Fact]
    public async Task Bench_this_in_the_briefing_carries_the_team_as_picked_so_far_and_lands_with_them_ticked_and_that_lead()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ENTRY", 1);
        await Until(app, id, s => s.Phase == CyclePhase.Briefing);

        (_, string dev) = await Get(client, $"/dev/legacy/{id}");
        (_, string play) = await Get(client, $"/legacy/{id}");

        // With nobody picked, the link names nobody; the browser adds the picks from the briefing's signals as they change.
        List<string> links = BenchLinks(dev);
        Assert.NotEmpty(links);
        Assert.All(links, l => Assert.DoesNotContain("crew=", l));
        string shown = WebUtility.HtmlDecode(dev);
        Assert.Contains("data-signals:_lead=\"''\"", shown);
        Assert.Contains("data-on:change=\"$_lead = el.value\"", shown);
        Assert.Contains($"data-attr:href=\"'{links[0]}' + $_team.split(' ').filter(c => c).map(c => '&crew=' + encodeURIComponent(c)).join('')", shown);
        Assert.DoesNotContain("Bench this", play);

        // The next briefing picks the last mission's team again, and its link carries them and the lead from the first draw.
        int cycle = (await app.Sessions.Read(app.Live, id))!.State.Cycle;
        await Until(app, id, s => s.Cycle > cycle && s.Phase == CyclePhase.Briefing);
        (_, dev) = await Get(client, $"/dev/legacy/{id}");
        string? picked = BenchLinks(dev).FirstOrDefault(l => l.Contains("crew="));
        Assert.NotNull(picked);
        Match lead = Regex.Match(picked, "&lead=([A-Z0-9_]+)");
        List<string> crew = Regex.Matches(picked, "&crew=([A-Z0-9_]+)").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(crew);
        Assert.True(!lead.Success || crew.Contains(lead.Groups[1].Value));

        (HttpStatusCode status, string page) = await Get(client, picked);
        Assert.Equal(HttpStatusCode.OK, status);
        LandsOn(page, Regex.Match(picked, "objective=([A-Z0-9_]+)").Groups[1].Value, id, crew, lead.Success ? lead.Groups[1].Value : crew[0]);
        guard.Verify();
    }

    [Fact]
    public async Task A_bench_this_link_with_picks_lands_with_those_members_ticked_and_that_lead()
    {
        using TestApp app = NewApp();
        Guid id = await app.CreateAtOrders("LEGACY_ENTRY", 1);
        string link = EditorLinks.Bench(id, Objective, ["CREW_01", "CREW_02"], "CREW_02");

        Assert.Equal($"/dev/tuning?objective={Objective}&team=legacy&source={id}&crew=CREW_01&crew=CREW_02&lead=CREW_02", link);
        (HttpStatusCode status, string page) = await Get(app.NewClient(), link);
        Assert.Equal(HttpStatusCode.OK, status);
        LandsOn(page, Objective, id, ["CREW_01", "CREW_02"], "CREW_02");

        // A lead who is not picked is left out, so the page takes the first picked.
        Assert.Equal($"/dev/tuning?objective={Objective}&team=legacy&source={id}&crew=CREW_01", EditorLinks.Bench(id, Objective, ["CREW_01"], "CREW_03"));
        guard.Verify();
    }

    [Fact]
    public async Task Bench_this_in_the_aftermath_names_the_mission_just_finished_its_team_and_its_lead()
    {
        using TestApp app = NewApp();
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ENTRY", 1);
        await Until(app, id, s => s.Phase == CyclePhase.Aftermath && s.Module<MissionsState>(MissionsModule.Name).Site?.Mission is not null);
        MissionsState missions = (await app.Sessions.Read(app.Live, id))!.State.Module<MissionsState>(MissionsModule.Name);
        MissionInFlight mission = missions.Site!.Mission!;
        string template = missions.Site.Objectives.Single(o => o.Id == mission.ObjectiveId).TemplateId;

        (_, string dev) = await Get(client, $"/dev/legacy/{id}");
        (_, string play) = await Get(client, $"/legacy/{id}");

        List<string> links = BenchLinks(dev);
        string link = Assert.Single(links);
        List<string> team = mission.State.Team.Select(m => m.Member.Id).ToList();
        Assert.StartsWith($"/dev/tuning?objective={template}&team=legacy&source={id}", link);
        Assert.Equal(team, Regex.Matches(link, "&crew=([A-Z0-9_]+)").Select(m => m.Groups[1].Value));
        Assert.Matches("&lead=[A-Z0-9_]+$", link);
        Assert.DoesNotContain("Bench this", play);

        (HttpStatusCode status, _) = await Get(client, link);
        Assert.Equal(HttpStatusCode.OK, status);
        guard.Verify();
    }
}

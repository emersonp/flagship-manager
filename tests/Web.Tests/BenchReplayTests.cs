using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using MissionBench;
using Persistence;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;
using Web.Tuning;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// Replay as the report (1f part one, Task 12; design 4.9): a replay is the run of the figures at that seed, under the tuning chosen; the
// worst seed is the worst run; its log is the bridge's own; a generated team is named by place and a real one by name; and nothing is
// written to any legacy.
public sealed class BenchReplayTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private const string Objective = "OBJECTIVE_TEMPLATE_1";

    private static TuningQuery Query(string text)
    {
        Dictionary<string, StringValues> values = Fields(text).GroupBy(f => f.Key).ToDictionary(g => g.Key, g => new StringValues(g.Select(f => f.Value).ToArray()));
        return TuningQuery.From(new QueryCollection(values));
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, SimJson.Options);

    private static TuningRuns Runs(TestApp app) => app.Services.GetRequiredService<TuningRuns>();

    private static TuningDrafts Drafts(TestApp app) => app.Services.GetRequiredService<TuningDrafts>();

    private static async Task<RunPlan> Plan(TestApp app, string query)
    {
        RunPrepared prepared = await Runs(app).Prepare(Query(query), false, CancellationToken.None);
        Assert.Empty(prepared.Errors);
        return prepared.Plan!;
    }

    private static Task<ReplayView> Ask(TestApp app, string query, ReplayAsk ask)
    {
        return BenchReplay.Ask(Runs(app), Drafts(app), Query(query), ask, CancellationToken.None);
    }

    // The harm BenchTally ranks the worst run by.
    private static int Harm(IEnumerable<(MemberStatus Status, int Injuries)> crew) => crew.Sum(c => c.Status == MemberStatus.Dead ? 10 : c.Status == MemberStatus.Incapacitated ? 1 : 0);

    // A legacy's mission played to its end, as ProseTests finds one.
    private static Played Ended(ContentPack pack)
    {
        for (ulong seed = 1; seed <= 80; seed++)
        {
            var played = new Played(pack, seed);
            if (played.StepUntil(() => played.State.Phase == CyclePhase.Aftermath && played.Mission?.State.Result is not null))
            {
                return played;
            }
        }

        throw new InvalidOperationException("No seed reached the aftermath.");
    }

    // A replay's beats are the run's: Bench.Replay at that seed, on the same inputs, under the tuning chosen, so it is that run of the
    // figures; and the log has a line for each beat.
    [Fact]
    public async Task A_replays_beats_are_the_runs_beats_at_that_seed_under_the_tuning_chosen()
    {
        using TestApp app = NewApp();
        Drafts(app).Set(TuningDrafts.Missions, "check.target", ["9"]);
        const string Words = $"objective={Objective}&runs=200&seed=3&strength=sweep&style=reckless&policy=bold&gear=scanner";
        RunPlan plan = await Plan(app, Words);
        BenchOptions veteran = plan.Options with { Strength = TeamStrength.Veteran };

        ReplayView draft = await Ask(app, Words, new ReplayAsk("57", true, "veteran"));
        ReplayView saved = await Ask(app, Words, new ReplayAsk("57", false, "veteran"));

        MissionTrace underDraft = Bench.Replay(plan.Draft!, plan.Draft!.Tuning, veteran, 57);
        MissionTrace underSaved = Bench.Replay(plan.Saved, plan.Saved.Tuning, veteran, 57);
        Assert.Empty(draft.Errors);
        Assert.Equal(Json(underDraft.Beats), Json(draft.Run!.Trace.Beats));
        Assert.Equal(Json(underDraft.Result), Json(draft.Run.Trace.Result));
        Assert.Equal(Json(underSaved.Beats), Json(saved.Run!.Trace.Beats));
        Assert.Equal(underDraft.Beats.Count, draft.Run.Log.Count);
        Assert.Equal((TeamStrength.Veteran, true, false), (draft.Run.Strength!.Value, draft.Run.Draft, saved.Run.Draft));
        // Each member's fate is the result's.
        Assert.Equal(underDraft.Result.Crew.Select(c => (c.Id, c.Status, c.Injury)), draft.Run.Fates.Select(f => (f.Id, f.Status, f.Injuries)));
    }

    // The worst seed under the draft names the run with the most harm among the draft's runs, the lowest seed among equals; and its
    // replay shows that harm.
    [Fact]
    public async Task The_worst_seed_under_the_draft_gives_the_worst_harm_among_the_drafts_runs()
    {
        using TestApp app = NewApp();
        Drafts(app).Set(TuningDrafts.Missions, "check.target", ["9"]);
        const string Words = $"objective={Objective}&runs=300&seed=1&strength=rookie";
        RunPlan plan = await Plan(app, Words);
        RunSnapshot snapshot = TuningRuns.Run(plan, null, TimeProvider.System, TimeSpan.Zero, CancellationToken.None);
        ulong worst = snapshot.Groups[0].Draft!.WorstSeed;

        List<int> harms = Enumerable.Range(1, 300)
            .Select(s => Harm(Bench.Replay(plan.Draft!, plan.Draft!.Tuning, plan.Options, (ulong)s).Result.Crew.Select(c => (c.Status, c.Injury))))
            .ToList();
        ReplayView replay = await Ask(app, Words, new ReplayAsk(worst.ToString(CultureInfo.InvariantCulture), true, ""));

        Assert.True(harms.Max() > 0);
        Assert.Equal(harms.IndexOf(harms.Max()) + 1, (int)worst);
        Assert.Equal(harms.Max(), Harm(replay.Run!.Fates.Select(f => (f.Status, f.Injuries))));
    }

    // The bench's log is the bridge's own: fed one test mission's beats, state, and seed, the replay's lines and the bridge's are the same,
    // prose, tags, rolls, and links included.
    [Fact]
    public void The_reports_prose_equals_the_bridges_for_the_same_beats()
    {
        ContentPack pack = BridgePack.Wide(BridgePack.Load());
        Played played = Ended(pack);
        var names = new Names(pack);
        List<MissionBeat> beats = played.CycleEvents().OfType<MissionBeat>().Where(b => b.MissionId == played.Mission!.Id).OrderBy(b => b.Index).ToList();
        string lead = played.CycleEvents().OfType<TeamAssigned>().Last().LeadId;

        IReadOnlyList<LogLine> bridge = BridgeReader.Read(played.Record(), pack, played.CycleEvents()).Mission!.Log;
        IReadOnlyList<LogLine> bench = BenchReplay.Log(pack, names, played.State.Seed, played.Mission!, beats, lead);

        Assert.NotEmpty(bench);
        Assert.Contains(bench, l => l.Authored);
        Assert.Equal(Json(bridge), Json(bench));
    }

    // A generated team is named by its place on the team, never as the game's crew its ids would name; a real team keeps its names.
    [Fact]
    public async Task A_generated_team_is_named_by_place_and_a_real_team_by_name()
    {
        using var app = new TestApp(developerMode: true, oddNames: true);
        string okafor = app.Live.Names.Crew("CREW_01");
        Assert.Contains(TestApp.OddName, okafor);

        ReplayView generated = await Ask(app, $"objective={Objective}&strength=rookie", new ReplayAsk("4", false, ""));
        Assert.Equal(["Lead", "Member 2", "Member 3"], generated.Run!.Fates.Select(f => f.Name));
        Assert.Equal(["CREW_01", "CREW_02", "CREW_03"], generated.Run.Fates.Select(f => f.Id));
        Assert.DoesNotContain(generated.Run.Log, l => string.Concat((l.Prose ?? []).Select(p => p.Text)).Contains($"{TestApp.OddName} CREW_0", StringComparison.Ordinal));

        Guid legacy = await app.CreateAtOrders("LEGACY_NAMES", 1);
        ReplayView real = await Ask(app, $"objective={Objective}&team=legacy&source={legacy}&crew=CREW_01&crew=CREW_02&lead=CREW_02", new ReplayAsk("4", false, ""));
        Assert.Empty(real.Errors);
        Assert.Null(real.Refused);
        Assert.Null(real.Run!.Strength);
        Assert.Equal([okafor, app.Live.Names.Crew("CREW_02")], real.Run.Fates.Select(f => f.Name));

        // On the page: the team line and the fates, then the Mission log.
        (HttpStatusCode status, string body) = await Post(app.NewClient(), $"/dev/tuning/replay?objective={Objective}&strength=rookie&replay-seed=4&replay-tuning=saved", "", datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("id=\"bench-replay\"", body);
        Assert.Contains("<span class=\"who\">Lead</span>", body);
        Assert.Contains("Lead, Member 2, and Member 3: a generated rookie team", body);
        Assert.Contains("id=\"mission-log\"", body);
        Assert.DoesNotContain("-3 CREW_01", body);
        Assert.Contains("Seed 4, rookie, under the saved tuning", body);
    }

    // The Replay well's own ask posts the inputs form, with the ask in the address; without script, its form carries both, and the
    // page comes back with the replay.
    [Fact]
    public async Task The_seed_box_replays_any_run_with_the_inputs_and_without_script_too()
    {
        using TestApp app = NewApp();
        RunPlan plan = await Plan(app, $"objective={Objective}&strength=veteran");
        MissionTrace trace = Bench.Replay(plan.Saved, plan.Saved.Tuning, plan.Options, 12);
        string verdict = Web.Components.Figures.Words(trace.Result.Outcome.ToString());

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/tuning/replay?replay-seed=12&replay-tuning=saved&replay-strength=", $"view=one&page=p1&objective={Objective}&strength=veteran", datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains($"<p class=\"replay__verdict\">{verdict}</p>", body);
        Assert.Contains("Seed 12, veteran, under the saved tuning", body);

        (status, body) = await Post(app.NewClient(), "/dev/tuning/replay", $"objective={Objective}&strength=veteran&replay-seed=12&replay-tuning=saved");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<title>", body);
        Assert.Contains($"<p class=\"replay__verdict\">{verdict}</p>", body);

        // A seed that is not one, and a draft that is not there, are refused beside their fields.
        (status, body) = await Post(app.NewClient(), "/dev/tuning/replay", $"objective={Objective}&replay-seed=x&replay-tuning=saved");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("Seed must be a whole number of 0 or more, not &#x27;x&#x27;.", body);
        (_, body) = await Post(app.NewClient(), "/dev/tuning/replay?replay-seed=3&replay-tuning=draft", $"view=one&objective={Objective}", datastar: true);
        Assert.Contains("There is no draft.", body);
        Assert.DoesNotContain("id=\"mission-log\"", body);
    }

    // Under each column the worst run's seed is a link that replays it with the run's inputs, under that column's tuning; a column with
    // no harmed run says so instead. Without script the link opens the page with the replay shown.
    [Fact]
    public async Task The_worst_runs_are_links_and_a_column_with_no_harm_says_so()
    {
        using TestApp app = NewApp();
        ContentPack pack = app.Live.Pack;
        Drafts(app).Set(TuningDrafts.Missions, "check.target", ["9"]);
        const string Words = $"objective={Objective}&runs=200&seed=1&strength=sweep";
        RunPlan plan = await Plan(app, Words);
        RunSnapshot snapshot = TuningRuns.Run(plan, null, TimeProvider.System, TimeSpan.Zero, CancellationToken.None);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/tuning/run", Words);
        Assert.Equal(HttpStatusCode.OK, status);
        foreach (RunGroup group in snapshot.Groups)
        {
            string strength = group.Strength!.Value.ToString().ToLowerInvariant();
            foreach ((BenchFigures figures, bool draft) in new[] { (group.Saved, false), (group.Draft!, true) })
            {
                if (figures.Deaths + figures.Incapacitated == 0)
                {
                    Assert.Contains($"unharmed\"{(draft ? " colspan=\"2\"" : "")} data-s=\"{strength}\">No run was harmed</td>", body);
                    continue;
                }

                string seed = figures.WorstSeed.ToString(CultureInfo.InvariantCulture);
                var ask = new ReplayAsk(seed, draft, strength);
                string href = BenchReplay.Page(Query(Words), ask).Replace("&", "&amp;", StringComparison.Ordinal);
                Assert.Contains($"<a href=\"{href}\"", body);
                Assert.Contains($"aria-label=\"Replay seed {seed}, {strength}, {(draft ? "draft" : "saved")}\">{seed}</a>", body);
            }
        }

        Assert.Contains("id=\"bench-replay\"", body);
        string first = snapshot.Groups[0].Draft!.WorstSeed.ToString(CultureInfo.InvariantCulture);
        (status, body) = await Get(app.NewClient(), BenchReplay.Page(Query(Words), new ReplayAsk(first, true, "rookie")));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains($"Seed {first}, rookie, under the draft", body);
        Assert.Contains("id=\"mission-log\"", body);

        // A single run nobody was harmed in.
        ulong calm = Enumerable.Range(1, 500).Select(s => (ulong)s)
            .First(s => Bench.Replay(pack, pack.Tuning, new BenchOptions(Objective) { Strength = TeamStrength.Veteran }, s).Result.Crew.All(c => c.Status == MemberStatus.Active));
        Drafts(app).Discard(TuningDrafts.Missions);
        (_, body) = await Post(app.NewClient(), "/dev/tuning/run", $"objective={Objective}&runs=1&seed={calm}&strength=veteran");
        Assert.Contains("<td class=\"n g1 unharmed\">No run was harmed</td>", body);
        guard.Verify();
    }

    // A replay of a legacy's team reads the legacy and writes nothing: its stored record and its event log are byte for byte the same.
    [Fact]
    public async Task Replaying_a_legacys_team_leaves_its_record_and_event_log_byte_for_byte_the_same()
    {
        using TestApp app = NewApp();
        Guid id = await app.CreateAtOrders("LEGACY_REPLAY", 1);
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != CyclePhase.Briefing)
            {
                legacy.Send(new AdvancePhase());
            }

            return legacy;
        });
        string before = LegacyExport.Export(app.Game.Store, id, app.Live.Game.ModuleTypes);
        int legacies = app.Game.Store.ListLegacies().Count;
        string team = $"objective={Objective}&team=legacy&source={id}&crew=CREW_01&crew=CREW_02&lead=CREW_02";

        (HttpStatusCode status, string body) = await Post(app.NewClient(), $"/dev/tuning/replay?{team}&replay-seed=8&replay-tuning=saved", "", datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("CREW_01 and CREW_02 (lead), as the launch would send them.", body);
        (status, _) = await Get(app.NewClient(), $"/dev/tuning?{team}&replay-seed=8");
        Assert.Equal(HttpStatusCode.OK, status);

        Assert.Equal(before, LegacyExport.Export(app.Game.Store, id, app.Live.Game.ModuleTypes));
        Assert.Equal(legacies, app.Game.Store.ListLegacies().Count);
    }
}

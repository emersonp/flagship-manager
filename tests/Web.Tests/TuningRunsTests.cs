using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using MissionBench;
using Sim.Core.Content;
using Sim.Missions;
using Web.Tuning;
using static Web.Tests.ContentFormsTests;

namespace Web.Tests;

// The bench run from the Tuning page (1f part one, Task 10; design 4.4 to 4.8), on a scratch copy of the fixture: the saved column is
// Bench.Run (and so SimRunner bench) on the same inputs; a half run's columns cover the same seeds; any change cancels a run; progress
// arrives before the end; a refused real team does not run; and the figures' rows are BenchReport.Summary's.
public sealed class TuningRunsTests(RepoContentGuard guard) : IClassFixture<RepoContentGuard>
{
    private const string Objective = "OBJECTIVE_TEMPLATE_1";

    private static TuningQuery Query(string text)
    {
        Dictionary<string, StringValues> values = Fields(text).GroupBy(f => f.Key).ToDictionary(g => g.Key, g => new StringValues(g.Select(f => f.Value).ToArray()));
        return TuningQuery.From(new QueryCollection(values));
    }

    private static string Json(BenchFigures figures) => JsonSerializer.Serialize(figures);

    private static TuningRuns Runs(TestApp app) => app.Services.GetRequiredService<TuningRuns>();

    private static TuningDrafts Drafts(TestApp app) => app.Services.GetRequiredService<TuningDrafts>();

    private static async Task<RunPlan> Plan(TestApp app, string query, bool savedOnly = false)
    {
        RunPrepared prepared = await Runs(app).Prepare(Query(query), savedOnly, CancellationToken.None);
        Assert.Empty(prepared.Errors);
        Assert.Null(prepared.Refused);
        return prepared.Plan!;
    }

    private static RunSnapshot RunAll(RunPlan plan, Action<RunSnapshot>? progress = null, CancellationToken cancellation = default)
    {
        return TuningRuns.Run(plan, progress, TimeProvider.System, TimeSpan.Zero, cancellation);
    }

    // The options as the command line would build them from the same words (SimRunner/Cli.cs), written out here rather than read
    // from TuningQuery, so the mapping is tested too.
    private static readonly BenchOptions Every = new(Objective)
    {
        Runs = 300,
        Seed = 5,
        Strength = TeamStrength.Rookie,
        Lead = LeadStyle.Reckless,
        Parameters = new Parameters(1, -1, true),
        GearTags = ["scanner"],
        Policy = CallPolicy.Bold,
        FixedDraw = true,
    };

    private const string EveryWords = $"objective={Objective}&runs=300&seed=5&strength=rookie&style=reckless&priority=1&autonomy=-1&develop=true&gear=scanner&policy=bold&draw=fixed";

    [Fact]
    public async Task The_saved_column_equals_Bench_Run_on_the_same_inputs_for_a_generated_team()
    {
        using TestApp app = NewApp();
        ContentPack pack = app.Live.Pack;
        RunPlan plan = await Plan(app, EveryWords);

        Assert.Equal(Every with { GearTags = plan.Options.GearTags }, plan.Options);
        Assert.Equal(["scanner"], plan.Options.GearTags);
        RunSnapshot snapshot = RunAll(plan);

        Assert.True(snapshot.Finished);
        RunGroup group = Assert.Single(snapshot.Groups);
        Assert.Null(group.Draft);
        Assert.Equal(Json(Bench.Run(pack, pack.Tuning, Every)), Json(group.Saved));
    }

    [Fact]
    public async Task The_saved_columns_of_a_sweep_equal_Bench_Run_for_each_strength()
    {
        using TestApp app = NewApp();
        ContentPack pack = app.Live.Pack;
        var options = new BenchOptions(Objective) { Runs = 250, Seed = 11 };

        RunSnapshot snapshot = RunAll(await Plan(app, $"objective={Objective}&runs=250&seed=11&strength=sweep"));

        Assert.Equal([TeamStrength.Rookie, TeamStrength.Mixed, TeamStrength.Veteran], snapshot.Groups.Select(g => g.Strength!.Value));
        foreach (RunGroup group in snapshot.Groups)
        {
            Assert.Equal(Json(Bench.Run(pack, pack.Tuning, options with { Strength = group.Strength!.Value })), Json(group.Saved));
        }
    }

    // A half run's columns have the same count of runs, and each is Bench.Run over that many seeds from the first: the same seeds.
    [Fact]
    public async Task A_half_runs_columns_have_the_same_runs_and_seeds()
    {
        using TestApp app = NewApp();
        ContentPack pack = app.Live.Pack;
        Drafts(app).Set(TuningDrafts.Missions, "check.target", ["9"]);
        RunPlan plan = await Plan(app, $"objective={Objective}&runs=3000&seed=4&strength=sweep");
        Assert.NotNull(plan.Draft);
        Assert.Equal(9, plan.Draft.Tuning.CheckTarget);

        using var cancel = new CancellationTokenSource();
        RunSnapshot half = RunAll(plan, _ => cancel.Cancel(), cancel.Token);

        Assert.Equal(TuningRuns.PatchEvery, half.Done);
        Assert.False(half.Finished);
        foreach (RunGroup group in half.Groups)
        {
            BenchOptions options = new(Objective) { Runs = half.Done, Seed = 4, Strength = group.Strength!.Value };
            Assert.Equal(half.Done, group.Saved.Runs);
            Assert.Equal(half.Done, group.Draft!.Runs);
            Assert.Equal(Json(Bench.Run(pack, pack.Tuning, options)), Json(group.Saved));
            Assert.Equal(Json(Bench.Run(plan.Draft, plan.Draft.Tuning, options)), Json(group.Draft));
        }
    }

    [Fact]
    public async Task Progress_arrives_before_the_end()
    {
        using TestApp app = NewApp();
        var seen = new List<int>();

        RunSnapshot last = RunAll(await Plan(app, $"objective={Objective}&runs=1200"), s => seen.Add(s.Done));

        Assert.Equal([500, 1000], seen);
        Assert.Equal(1200, last.Done);
    }

    // Through the real app: the stream patches the figures as the run goes, then the end, with BenchNoise's rule under a draft's table.
    [Fact]
    public async Task The_stream_patches_progress_before_the_end()
    {
        using TestApp app = NewApp();
        Drafts(app).Set(TuningDrafts.Missions, "check.target", ["9"]);

        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/tuning/run?page=p1", $"objective={Objective}&runs=6000", datastar: true);

        Assert.Equal(HttpStatusCode.OK, status);
        // Some figures while running: more than none and fewer than all.
        int running = Regex.Match(body, "<span class=\"m\">[1-9][0-9,]*</span> of <span class=\"m\">6,000</span> runs in each column, from seed <span class=\"m\">1</span>; running</p>").Index;
        int end = body.IndexOf("<span class=\"m\">6,000</span> of <span class=\"m\">6,000</span> runs in each column, from seed <span class=\"m\">1</span></p>", StringComparison.Ordinal);
        Assert.True(running > 0, "no progress line while running");
        Assert.True(end > running, "the end is not after the progress");
        Assert.Contains("id=\"figs-summary\"", body[running..end]);
        Assert.Contains(BenchNoise.Rule, body);
        Assert.Contains("<th scope=\"col\" class=\"n chg\">Change</th>", body[end..]);
        Assert.False(Runs(app).Running("p1"));

        // The scratch copy was benched; the repository's content is as it was.
        guard.Verify();
    }

    [Fact]
    public async Task A_draft_change_cancels_a_running_run_and_so_does_a_new_run_on_the_same_page()
    {
        using TestApp app = NewApp();
        TuningRuns runs = Runs(app);

        // A run going on a background task, stopped by the draft once its first figures are in.
        RunPlan plan = await Plan(app, $"objective={Objective}&runs=100000");
        RunTicket ticket = runs.Begin("p1", CancellationToken.None);
        using var first = new SemaphoreSlim(0);
        Task<RunSnapshot> work = Task.Run(() => RunAll(plan, _ => first.Release(), ticket.Token));
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(30)));
        Drafts(app).Set(TuningDrafts.Missions, "check.target", ["9"]);
        RunSnapshot stopped = await work;
        Assert.False(stopped.Finished);
        Assert.Equal(RunStop.Draft, ticket.Stop);
        runs.End(ticket);

        // A new run on the page stops the one before; another page's run goes on.
        RunTicket one = runs.Begin("p1", CancellationToken.None);
        RunTicket other = runs.Begin("p2", CancellationToken.None);
        RunTicket two = runs.Begin("p1", CancellationToken.None);
        Assert.True(one.Token.IsCancellationRequested);
        Assert.Equal(RunStop.NewRun, one.Stop);
        Assert.False(two.Token.IsCancellationRequested);
        Assert.False(other.Token.IsCancellationRequested);

        // A change of input stops the page's run.
        (HttpStatusCode status, string body) = await Post(app.NewClient(), "/dev/tuning/inputs?page=p1", $"objective={Objective}&runs=500", datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(two.Token.IsCancellationRequested);
        Assert.Equal(RunStop.Inputs, two.Stop);
        Assert.Contains("id=\"bench-inputs\"", body);
        Assert.Contains("history.replaceState(null, &#x27;&#x27;, &#x27;/dev/tuning?objective=OBJECTIVE_TEMPLATE_1&amp;runs=500&#x27;)", body);
        Assert.False(other.Token.IsCancellationRequested);
    }

    // A test ship's unfit member is shown with the launch's words and a disabled box, and a team that picks them is refused, in the
    // same words, without running.
    [Fact]
    public async Task A_refused_real_team_shows_its_refusal_and_does_not_run()
    {
        using TestApp app = NewApp();
        const string Ship = "team=testship&source=test_ship_a";

        (HttpStatusCode status, string body) = await Get(app.NewClient(), $"/dev/tuning?{Ship}&crew=CREW_01");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<td>Crew member &#x27;CREW_04&#x27; is not fit.</td>", body);
        Assert.Matches("<input type=\"checkbox\" name=\"crew\" value=\"CREW_04\" disabled", body);
        Assert.Matches("<input type=\"checkbox\" name=\"crew\" value=\"CREW_01\" checked", body);

        (status, body) = await Post(app.NewClient(), "/dev/tuning/run?page=p1", $"objective={Objective}&runs=200&{Ship}&crew=CREW_01&crew=CREW_04&lead=CREW_01", datastar: true);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("<p class=\"error\" id=\"bench-refused\" role=\"alert\">Crew member &#x27;CREW_04&#x27; is not fit.</p>", body);
        Assert.DoesNotContain("id=\"bench-figures\"", body);
        Assert.False(Runs(app).Running("p1"));

        // The same team without them runs.
        RunPrepared prepared = await Runs(app).Prepare(Query($"objective={Objective}&runs=200&{Ship}&crew=CREW_01&crew=CREW_02"), false, CancellationToken.None);
        Assert.NotNull(prepared.Plan);
        Assert.Equal(["CREW_01", "CREW_02"], prepared.Plan.Options.Team!.Members.Select(m => m.Id));
        Assert.Equal([null], prepared.Plan.Groups);
    }

    [Fact]
    public async Task A_draft_that_does_not_load_refuses_Run_and_offers_the_saved_tuning_alone()
    {
        using TestApp app = NewApp();
        Drafts(app).Set(TuningDrafts.Missions, "compounding.collapseAt", ["99"]);

        RunPrepared refused = await Runs(app).Prepare(Query($"objective={Objective}"), false, CancellationToken.None);
        Assert.Null(refused.Plan);
        (_, string body) = await Get(app.NewClient(), "/dev/tuning");
        Assert.Contains("Run saved only", body);
        Assert.Contains("The draft does not load, so it cannot run.", body);

        RunPlan saved = await Plan(app, $"objective={Objective}", savedOnly: true);
        Assert.Null(saved.Draft);
    }

    [Fact]
    public async Task An_input_the_bench_cannot_take_is_refused_beside_it_in_the_command_lines_words()
    {
        using TestApp app = NewApp();

        RunPrepared prepared = await Runs(app).Prepare(Query("objective=NOPE&runs=0&seed=-1&gear=warp"), false, CancellationToken.None);

        Assert.Null(prepared.Plan);
        Assert.StartsWith("Unknown objective 'NOPE'.", prepared.Errors["objective"]);
        Assert.Equal("Runs must be a whole number from 1 to 10,000,000, not '0'.", prepared.Errors["runs"]);
        Assert.Equal("Seed must be a whole number of 0 or more, not '-1'.", prepared.Errors["seed"]);
        Assert.StartsWith("Unknown gear tag 'warp'.", prepared.Errors["gear"]);
        (_, string body) = await Post(app.NewClient(), "/dev/tuning/run?page=p1", "objective=NOPE&runs=0", datastar: true);
        Assert.Contains("<p class=\"error\" id=\"bi-runs-error\">Runs must be a whole number from 1 to 10,000,000, not &#x27;0&#x27;.</p>", body);

        // A 64-bit seed is taken as text, whole.
        RunPlan plan = await Plan(app, $"objective={Objective}&seed=18446744073709551615&runs=1");
        Assert.Equal(ulong.MaxValue, plan.Options.Seed);
    }

    // FigureRows is BenchReport.Summary's row list, kept equal by this test rather than shared: the same labels, in the same order, with
    // the same values as the command line prints them, for one column and for two.
    [Fact]
    public void FigureRows_labels_and_values_equal_BenchReport_Summarys_rows()
    {
        using TestApp app = NewApp();
        ContentPack pack = app.Live.Pack;
        var options = new BenchOptions(Objective) { Runs = 400, Strength = TeamStrength.Rookie };
        BenchFigures saved = Bench.Run(pack, pack.Tuning, options);
        BenchFigures draft = Bench.Run(pack, pack.Tuning with { CheckTarget = pack.Tuning.CheckTarget + 2 }, options);

        foreach (BenchFigures[] columns in new[] { new[] { saved }, new[] { saved, draft } })
        {
            string text = BenchReport.Summary(columns.Select((f, i) => ($"c{i}", f)).ToList(), showChange: columns.Length == 2).ReplaceLineEndings("\n");
            List<string> lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(l => !l.StartsWith("Means", StringComparison.Ordinal)).ToList();
            IReadOnlyList<FigureRow> rows = FigureRows.Summary(columns);

            Assert.Equal(lines.Count, rows.Count);
            foreach ((FigureRow row, string line) in rows.Zip(lines))
            {
                Assert.StartsWith(row.Label + " ", line);
                List<string> values = [.. columns.Select(c => FigureRows.Number(row.Value(c), row.Format))];
                if (columns.Length == 2)
                {
                    // The page writes a true minus where the command line writes a hyphen.
                    values.Add(FigureRows.Change(row.Value(saved), row.Value(draft), row.Format).Replace('−', '-'));
                }

                Assert.Equal(values, line[row.Label.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
    }

    [Fact]
    public void Every_row_has_its_kind_from_BenchNoise()
    {
        using TestApp app = NewApp();
        ContentPack pack = app.Live.Pack;
        BenchFigures figures = Bench.Run(pack, pack.Tuning, new BenchOptions(Objective) { Runs = 50 });
        string encounter = figures.Encounters[0].EncounterId;

        IReadOnlyList<FigureRow> summary = FigureRows.Summary([figures]);
        IReadOnlyList<FigureRow> rows = FigureRows.Encounter(encounter);

        Assert.All(summary.Where(r => r.Label.EndsWith(" %", StringComparison.Ordinal)), r => Assert.Equal(FigureKind.Share, BenchNoise.KindOf(r.Row)));
        Assert.All(summary.Where(r => r.Label.Contains("per 100", StringComparison.Ordinal)), r => Assert.Equal(FigureKind.Rate, BenchNoise.KindOf(r.Row)));
        Assert.All(summary.Where(r => r.Label.StartsWith("Experience", StringComparison.Ordinal) || r.Label.Contains("per mission", StringComparison.Ordinal) || r.Label.Contains("entering", StringComparison.Ordinal)), r => Assert.Equal(FigureKind.Mean, BenchNoise.KindOf(r.Row)));
        Assert.Equal(["Checks", "Approach 1 %", "Approach 2 %", "Approach 3 %", "Approach 4 %", "Narrowed %", "Critical %", "Success %", "Partial %", "Failure %", "Disaster %", "Deaths", "Incapacitated"], rows.Select(r => r.Label));
        Assert.Equal(BenchRow.EncounterChecks, rows[0].Row);
        Assert.Equal(figures.Encounters[0].Checks.ToString("#,0", CultureInfo.InvariantCulture), FigureRows.Number(rows[0].Value(figures), rows[0].Format));
        Assert.Equal((figures.Encounters[0].Checks, (double)figures.Encounters[0].ChecksSquares, 50), rows[0].Noise(figures));
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench.Tests;

public class BenchTests
{
    private static readonly ContentPack Pack = PrototypeCrew.Pack;

    // Every field of the figures Run gave before slice 1f part one moved its loop into a tally (Task 1), on the fixture and the game's
    // pack, in tests/MissionBench.Tests/golden-figures.txt. The spread (the sums of squares) came later and is left out here. If a
    // change is intended, accept it in the same commit: the test writes what it found beside the file, as golden-figures.received.txt.
    [Fact]
    public void Golden_figures_match_the_accepted_ones()
    {
        ContentPack game = LoadGame();
        (string Name, ContentPack Pack, BenchOptions Options)[] cases =
        [
            ("fixture mixed", Pack, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 400, Seed = 3 }),
            ("fixture rookie reckless fixed scanner", Pack, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 300, Strength = TeamStrength.Rookie, Lead = LeadStyle.Reckless, FixedDraw = true, GearTags = ["scanner"] }),
            ("fixture veteran bold develop", Pack, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 300, Strength = TeamStrength.Veteran, Policy = CallPolicy.Bold, Parameters = new Parameters(DevelopJuniors: true) }),
            ("fixture real team withdraw", Pack, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 300, Team = PrototypeCrew.Team(2, 2, 5, 7), Policy = CallPolicy.Withdraw }),
            ("game 1 rookie", game, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 500, Strength = TeamStrength.Rookie }),
            ("game 1 veteran", game, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 500, Strength = TeamStrength.Veteran }),
            ("game 2 mixed", game, new BenchOptions("OBJECTIVE_TEMPLATE_2") { Runs = 500, Seed = 9 }),
        ];
        string actual = string.Concat(cases.Select(c => $"{c.Name}: {WithoutSpread(Bench.Run(c.Pack, c.Pack.Tuning, c.Options))}\n"));
        string path = Path.Combine(PrototypeCrew.Root(), "tests", "MissionBench.Tests", "golden-figures.txt");
        string received = Path.ChangeExtension(path, ".received.txt");
        string expected = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : "";
        if (actual == expected)
        {
            File.Delete(received);
        }
        else
        {
            File.WriteAllText(received, actual);
        }

        Assert.Equal(expected, actual);
    }

    // A tally can be read part way, so a run in two halves is the same as one run (slice 1f part one, design 4.7).
    [Fact]
    public void A_tally_fed_in_two_halves_gives_the_figures_of_one_fed_at_once()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 300, Seed = 2, Strength = TeamStrength.Rookie };
        MissionTrace[] traces = Enumerable.Range(0, options.Runs).Select(i => Bench.Replay(Pack, Pack.Tuning, options, options.Seed + (ulong)i)).ToArray();

        var once = new BenchTally(Pack);
        foreach (MissionTrace trace in traces)
        {
            once.Add(trace);
        }

        var halves = new BenchTally(Pack);
        foreach (MissionTrace trace in traces.Take(150))
        {
            halves.Add(trace);
        }

        BenchFigures half = halves.Figures();
        foreach (MissionTrace trace in traces.Skip(150))
        {
            halves.Add(trace);
        }

        Assert.Equal((150, 300), (half.Runs, halves.Runs));
        Assert.Equal(150, half.Outcomes.Values.Sum());
        Assert.Equal(JsonSerializer.Serialize(once.Figures()), JsonSerializer.Serialize(halves.Figures()));
        Assert.Equal(JsonSerializer.Serialize(Bench.Run(Pack, Pack.Tuning, options)), JsonSerializer.Serialize(once.Figures()));
    }

    // Fed out of seed order, the worst seed is still the lowest seed with the most harm.
    [Fact]
    public void The_worst_seed_does_not_depend_on_the_order_runs_are_fed()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 200, Strength = TeamStrength.Rookie };
        MissionTrace[] traces = Enumerable.Range(0, options.Runs).Select(i => Bench.Replay(Pack, Pack.Tuning, options, options.Seed + (ulong)i)).ToArray();
        var reversed = new BenchTally(Pack);
        foreach (MissionTrace trace in traces.Reverse())
        {
            reversed.Add(trace);
        }

        Assert.Equal(Bench.Run(Pack, Pack.Tuning, options).WorstSeed, reversed.Figures().WorstSeed);
    }

    [Fact]
    public void Cancellation_stops_a_run_within_a_hundred_runs()
    {
        using var cancel = new CancellationTokenSource();
        var seen = new List<int>();
        var progress = new Recorder(done =>
        {
            seen.Add(done);
            if (done >= 250)
            {
                cancel.Cancel();
            }
        });

        Assert.ThrowsAny<OperationCanceledException>(() => Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 1000 }, progress, cancel.Token));
        Assert.Equal([100, 200, 300], seen);
    }

    [Fact]
    public void A_cancelled_token_runs_nothing()
    {
        var seen = new List<int>();

        Assert.ThrowsAny<OperationCanceledException>(() => Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 50 }, new Recorder(seen.Add), new CancellationToken(canceled: true)));
        Assert.Empty(seen);
    }

    [Fact]
    public void Progress_arrives_before_the_end_and_at_it()
    {
        var seen = new List<int>();

        Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 250 }, new Recorder(seen.Add));

        Assert.Equal([100, 200, 250], seen);
    }

    // The sums of squares the noise test needs (design 4.8), one term a run.
    [Fact]
    public void The_spread_is_the_sum_of_squares_over_the_runs()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 120, Seed = 7 };
        MissionTrace[] traces = Enumerable.Range(0, options.Runs).Select(i => Bench.Replay(Pack, Pack.Tuning, options, options.Seed + (ulong)i)).ToArray();

        BenchFigures figures = Bench.Run(Pack, Pack.Tuning, options);

        Assert.Equal(traces.Sum(t => (long)t.Result.Calls * t.Result.Calls), figures.CallsSquares);
        Assert.Equal(traces.Sum(t => (long)t.Result.Initiatives * t.Result.Initiatives), figures.InitiativesSquares);
        Assert.True(figures.CallsSquares >= figures.Calls && figures.InitiativesSquares >= figures.Initiatives);
        foreach ((string id, double squares) in figures.ExperienceSquaresByMember)
        {
            double expected = traces.Sum(t => t.Result.Crew.Where(c => c.Id == id).Sum(c => Math.Pow(c.Experience.Values.Sum(), 2)));
            Assert.Equal(expected, squares, 1e-9);
        }

        Assert.Equal(figures.ExperienceByMember.Keys, figures.ExperienceSquaresByMember.Keys);
        foreach (StageFigures stage in figures.Stages)
        {
            StageBeat[] reached = traces.SelectMany(t => t.Beats.OfType<StageBeat>().Where(b => b.Cursor == stage.Position - 1)).ToArray();
            Assert.Equal(reached.Length, stage.Reached);
            Assert.Equal(reached.Sum(b => (double)b.Momentum * b.Momentum), stage.MomentumSquares, 1e-9);
            Assert.Equal(reached.Sum(b => (double)b.Strain * b.Strain), stage.StrainSquares, 1e-9);
        }
    }

    // The draft column benches pack with { Bench = draft's bench } (Task 10), so an edit to bench.teams is measured: generated teams
    // follow the pack's bench block, not the tuning's.
    [Fact]
    public void Generated_teams_follow_the_packs_bench_block()
    {
        ContentPack bigger = Pack with { Bench = Pack.Bench with { Mixed = Pack.Bench.Mixed with { Size = 4 } } };
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 40 };

        Assert.Equal(40 * 3, Bench.Run(Pack, Pack.Tuning, options).CrewMissions);
        Assert.Equal(40 * 4, Bench.Run(bigger, Pack.Tuning, options).CrewMissions);
        Assert.Equal(4, Bench.Replay(bigger, Pack.Tuning, options, 1).Team.Members.Count);
    }

    [Fact]
    public void Every_objective_runs_each_strength_in_the_packs_order()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 30, Seed = 4 };
        var seen = new List<int>();

        IReadOnlyList<ObjectiveCell> cells = Bench.Objectives(Pack, Pack.Tuning, options, [TeamStrength.Rookie, TeamStrength.Veteran], new Recorder(seen.Add));

        Assert.Equal(Pack.Objectives.SelectMany(o => new[] { (o.Id, (TeamStrength?)TeamStrength.Rookie), (o.Id, TeamStrength.Veteran) }), cells.Select(c => (c.ObjectiveId, c.Strength)));
        Assert.All(cells, c => Assert.Null(c.Refused));
        ObjectiveCell second = cells.Single(c => c.ObjectiveId == "OBJECTIVE_TEMPLATE_2" && c.Strength == TeamStrength.Veteran);
        Assert.Equal(
            JsonSerializer.Serialize(Bench.Run(Pack, Pack.Tuning, options with { ObjectiveId = "OBJECTIVE_TEMPLATE_2", Strength = TeamStrength.Veteran })),
            JsonSerializer.Serialize(second.Figures));
        Assert.Equal(cells.Count * 30, seen[^1]);
        Assert.Equal(seen.Order(), seen);
    }

    // A real team gets one cell an objective, refused in the launch check's words where its size does not fit.
    [Fact]
    public void Every_objective_refuses_a_real_team_of_the_wrong_size()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 20, Team = PrototypeCrew.Team(1, 1, 2, 3, 4) };

        IReadOnlyList<ObjectiveCell> cells = Bench.Objectives(Pack, Pack.Tuning, options, [TeamStrength.Rookie, TeamStrength.Veteran]);

        Assert.Equal(["OBJECTIVE_TEMPLATE_1", "OBJECTIVE_TEMPLATE_2"], cells.Select(c => c.ObjectiveId));
        Assert.All(cells, c => Assert.Null(c.Strength));
        Assert.Equal((20, (string?)null), (cells[0].Figures!.Runs, cells[0].Refused));
        Assert.Equal(((BenchFigures?)null, "A team of 4 is outside the 2 to 3 this objective takes."), (cells[1].Figures, cells[1].Refused));
    }

    [Fact]
    public void A_trace_keeps_the_missions_last_state()
    {
        MissionTrace trace = Bench.Replay(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1"), 3);

        Assert.NotNull(trace.State);
        Assert.Equal((trace.Seed, trace.Result.Outcome), (trace.State.Seed, trace.State.Result?.Outcome));
    }

    [Fact]
    public void The_same_seeds_give_the_same_figures()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 200, Seed = 5 };

        BenchFigures first = Bench.Run(Pack, Pack.Tuning, options);
        BenchFigures second = Bench.Run(Pack, Pack.Tuning, options);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(200, first.Outcomes.Values.Sum());
        Assert.Equal(600, first.CrewMissions);
    }

    [Fact]
    public void A_harsher_tuning_moves_the_figures_on_the_same_seeds()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 300 };

        BenchFigures before = Bench.Run(Pack, Pack.Tuning, options);
        BenchFigures after = Bench.Run(Pack, Pack.Tuning with { CheckTarget = 9 }, options);

        Assert.True(after.Outcomes[MissionOutcome.FullSuccess] < before.Outcomes[MissionOutcome.FullSuccess]);
        Assert.Equal(before.Encounters.Select(e => e.EncounterId), after.Encounters.Select(e => e.EncounterId).Where(id => id != "ENCOUNTER_CHAIN"));
    }

    // Each encounter's checks are counted by the place of the approach taken, always four places, and the table heads them by place.
    [Fact]
    public void Checks_are_counted_by_the_approachs_place_in_its_encounter()
    {
        BenchFigures figures = Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 50 });

        Assert.All(figures.Encounters, e =>
        {
            Assert.Equal(4, e.ByPosition.Count);
            Assert.Equal(e.Checks, e.ByPosition.Sum());
            Assert.Equal(0, e.ByPosition[2] + e.ByPosition[3]);
        });
        Assert.Matches(@"^Encounter\s+checks\s+1st %\s+2nd %\s+3rd %\s+4th %\s+narrowed %\s+crit %", BenchReport.Encounters(figures));
    }

    // A check is narrowed when a requirement left fewer approaches on offer than the encounter has.
    [Fact]
    public void Checks_with_an_approach_unavailable_are_counted_as_narrowed()
    {
        ContentPack narrowed = Pack with
        {
            Encounters = Pack.Encounters.Select(e => e with { Approaches = [e.Approaches[0], e.Approaches[1] with { Requires = [new RequirementDef(null, 0, "nothing")] }] }).ToList(),
        };

        BenchFigures open = Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 50 });
        BenchFigures closed = Bench.Run(narrowed, narrowed.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 50 });

        Assert.All(open.Encounters, e => Assert.Equal(0, e.Narrowed));
        Assert.All(closed.Encounters, e => Assert.Equal((e.Checks, e.Checks, 0), (e.Narrowed, e.ByPosition[0], e.ByPosition[1])));
        Assert.Matches(@"^Encounter\s+checks\s+1st %\s+2nd %\s+3rd %\s+4th %\s+narrowed %\s+crit %", BenchReport.Encounters(closed));
        Assert.Matches(@"\n\S+\s+\d+\s+100\s+0\s+0\s+0\s+100\s", BenchReport.Encounters(closed));
    }

    [Fact]
    public void The_summary_names_encounter_positions_and_how_many_runs_reached_them()
    {
        BenchFigures figures = Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 50 });

        string summary = BenchReport.Summary([("mixed", figures)], showChange: false);

        Assert.Matches(@"Reached encounter 1 %\s+100\.0\r?\nMomentum entering encounter 1\s+\S+\r?\nStrain entering encounter 1", summary);
        Assert.DoesNotContain("stage", summary, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("Means are over the missions that reached each encounter. A chained encounter shifts later positions." + Environment.NewLine, summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Replay_text_does_not_follow_the_current_culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        try
        {
            var odd = (CultureInfo)new CultureInfo("de-DE").Clone();
            odd.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = odd;

            string text = string.Concat(Enumerable.Range(1, 30).Select(seed => BenchReport.Replay(Bench.Replay(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1"), (ulong)seed))));

            Assert.Contains("margin -", text, StringComparison.Ordinal);
            Assert.DoesNotContain("~", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\d,\d", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Veterans_succeed_more_often_than_rookies()
    {
        int FullSuccesses(TeamStrength strength)
        {
            return Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 300, Strength = strength }).Outcomes[MissionOutcome.FullSuccess];
        }

        Assert.True(FullSuccesses(TeamStrength.Rookie) < FullSuccesses(TeamStrength.Mixed));
        Assert.True(FullSuccesses(TeamStrength.Mixed) < FullSuccesses(TeamStrength.Veteran));
    }

    // Rookies never qualify for supervision, so their case is equality.
    [Theory]
    [InlineData(TeamStrength.Rookie)]
    [InlineData(TeamStrength.Mixed)]
    [InlineData(TeamStrength.Veteran)]
    public void Develop_juniors_never_beats_best_qualified_on_generated_teams(TeamStrength strength)
    {
        int FullSuccesses(bool develop)
        {
            var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 2000, Strength = strength, Parameters = new Parameters(DevelopJuniors: develop) };
            return Bench.Run(Pack, Pack.Tuning, options).Outcomes[MissionOutcome.FullSuccess];
        }

        Assert.True(FullSuccesses(develop: true) <= FullSuccesses(develop: false));
    }

    [Fact]
    public void A_fixed_draw_meets_the_same_encounters_every_run()
    {
        BenchFigures figures = Bench.Run(Pack, Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 100, Strength = TeamStrength.Veteran, FixedDraw = true });

        Assert.Equal(5, figures.Encounters.Count(e => e.EncounterId != "ENCOUNTER_CHAIN"));
    }

    [Fact]
    public void A_run_is_named_by_its_seed_and_replays_the_same()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 50, Strength = TeamStrength.Rookie };
        ulong worst = Bench.Run(Pack, Pack.Tuning, options).WorstSeed;

        MissionTrace first = Bench.Replay(Pack, Pack.Tuning, options, worst);
        MissionTrace second = Bench.Replay(Pack, Pack.Tuning, options, worst);

        Assert.Equal(BenchReport.Replay(first), BenchReport.Replay(second));
        Assert.StartsWith($"Seed {worst}. Lead CREW_01.", BenchReport.Replay(first), StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_teams_follow_the_bench_tuning_and_the_lead_style()
    {
        Team mixed = TeamGenerator.Generate(Pack.Bench, TeamStrength.Mixed, LeadStyle.Reckless, Pack.Skills, seed: 3);
        Team again = TeamGenerator.Generate(Pack.Bench, TeamStrength.Mixed, LeadStyle.Reckless, Pack.Skills, seed: 3);

        Assert.Equal(JsonSerializer.Serialize(mixed), JsonSerializer.Serialize(again));
        Assert.Equal(("CREW_01", 3), (mixed.LeadId, mixed.Members.Count));
        Assert.Equal((-2, -1), (mixed.Members[0].Caution, mixed.Members[0].Discipline));
        Assert.All(mixed.Members[0].Skills.Values, level => Assert.InRange(level, 5, 6));
        Assert.All(mixed.Members.Skip(1).SelectMany(m => m.Skills.Values), level => Assert.InRange(level, 2, 4));
        Assert.All(mixed.Members.Skip(1), m => Assert.InRange(m.Caution, -1, 1));
    }

    [Fact]
    public void Each_policy_answers_calls_its_own_way()
    {
        var call = new PendingCall("DECISION_PROMPT_1", 3, 1.5, "CREW_01", CallAnswer.Bold, [CallAnswer.Bold, CallAnswer.Safe, CallAnswer.Withdraw], "dec:0");
        PendingCall abort = call with { DecisionId = DecisionIds.Abort, Options = [CallAnswer.Bold, CallAnswer.Safe] };

        Assert.Equal(CallAnswer.Bold, Bench.Answer(CallPolicy.BackLead, call));
        Assert.Equal(CallAnswer.Safe, Bench.Answer(CallPolicy.BackLead, abort));
        Assert.Equal(CallAnswer.Safe, Bench.Answer(CallPolicy.Safe, call));
        Assert.Equal(CallAnswer.Withdraw, Bench.Answer(CallPolicy.Withdraw, call));
        Assert.Equal(CallAnswer.Safe, Bench.Answer(CallPolicy.Withdraw, abort));
    }

    [Fact]
    public void The_summary_shows_before_after_and_change()
    {
        var options = new BenchOptions("OBJECTIVE_TEMPLATE_1") { Runs = 100 };
        BenchFigures figures = Bench.Run(Pack, Pack.Tuning, options);

        // The report ends lines with Environment.NewLine (CRLF on Windows); compare by line, not by "\n".
        string same = BenchReport.Summary([("before", figures), ("after", figures)], showChange: true).ReplaceLineEndings("\n");

        Assert.Contains("before", same.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("change", same.Split('\n')[0], StringComparison.Ordinal);
        Assert.All(same.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(r => !r.StartsWith("Means", StringComparison.Ordinal)), row => Assert.Matches(@"(\+0\.0+|-)$", row));
    }

    private static ContentPack LoadGame()
    {
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(PrototypeCrew.Root(), "content"));
        return result.Pack ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => $"{e.File}: {e.Message}")));
    }

    // The figures as JSON, without the sums of squares the noise test needs.
    private static string WithoutSpread(BenchFigures figures)
    {
        JsonObject json = JsonSerializer.SerializeToNode(figures)!.AsObject();
        json.Remove("CallsSquares");
        json.Remove("InitiativesSquares");
        json.Remove("ExperienceSquaresByMember");
        foreach (JsonNode? stage in json["Stages"]!.AsArray())
        {
            stage!.AsObject().Remove("MomentumSquares");
            stage.AsObject().Remove("StrainSquares");
        }

        return json.ToJsonString();
    }

    // Reports at once on the caller's thread, unlike Progress<T>, so a test sees each report as it is made.
    private sealed class Recorder(Action<int> report) : IProgress<int>
    {
        public void Report(int value)
        {
            report(value);
        }
    }
}

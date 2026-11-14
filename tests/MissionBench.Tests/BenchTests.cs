using System.Globalization;
using System.Text.Json;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench.Tests;

public class BenchTests
{
    private static readonly ContentPack Pack = PrototypeCrew.Pack;

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
        PendingCall abort = call with { DecisionId = MissionResolver.AbortDecisionId, Options = [CallAnswer.Bold, CallAnswer.Safe] };

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

        string same = BenchReport.Summary([("before", figures), ("after", figures)], showChange: true);

        Assert.Contains("before", same.Split('\n')[0], StringComparison.Ordinal);
        Assert.Contains("change", same.Split('\n')[0], StringComparison.Ordinal);
        Assert.All(same.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Where(r => !r.StartsWith("Means", StringComparison.Ordinal)), row => Assert.Matches(@"(\+0\.0+|-)$", row));
    }
}

using Sim.Missions;

namespace MissionBench.Tests;

// The accepted shape of the mission numbers (GDD 4.4 and 4.7), on the crew they were first benched on.
// The bands are wide on purpose: they catch a rule breaking, not a number being tuned.
public class ShapeTests
{
    [Theory]
    [InlineData(1, new[] { 1, 3, 5 }, 2.6, 3.6, 2.0, 3.0, 0.8, 1.8)]
    [InlineData(3, new[] { 3, 4, 5 }, 2.0, 3.0, 0.8, 1.8, 0.3, 1.1)]
    [InlineData(2, new[] { 2, 3, 5 }, 0.4, 1.2, 0.0, 0.5, 0.0, 0.5)]
    public void Calls_per_mission_fall_as_the_lead_gets_bolder_and_autonomy_rises(int lead, int[] team, double lowMin, double lowMax, double midMin, double midMax, double highMin, double highMax)
    {
        double[] calls = new[] { -1, 0, 1 }.Select(a => Calls(Run(lead, team, new Parameters(Autonomy: a)))).ToArray();

        Assert.InRange(calls[0], lowMin, lowMax);
        Assert.InRange(calls[1], midMin, midMax);
        Assert.InRange(calls[2], highMin, highMax);
    }

    [Fact]
    public void A_reckless_lead_loses_more_and_calls_less_than_a_cautious_one()
    {
        BenchFigures cautious = Run(1, [1, 3, 5], new Parameters());
        BenchFigures reckless = Run(2, [2, 3, 5], new Parameters());

        Assert.InRange(100.0 * reckless.Outcomes[MissionOutcome.Loss] / reckless.Runs, 8, 24);
        Assert.InRange(100.0 * reckless.Deaths / reckless.CrewMissions, 2, 6);
        Assert.True(cautious.Outcomes[MissionOutcome.Loss] < reckless.Outcomes[MissionOutcome.Loss]);
    }

    [Fact]
    public void Develop_juniors_costs_some_success_and_teaches_the_juniors()
    {
        BenchFigures best = Run(3, [3, 5, 6], new Parameters());
        BenchFigures develop = Run(3, [3, 5, 6], new Parameters(DevelopJuniors: true));

        double gap = 100.0 * (best.Outcomes[MissionOutcome.FullSuccess] - develop.Outcomes[MissionOutcome.FullSuccess]) / best.Runs;
        Assert.InRange(gap, 2, 20);
        Assert.True(develop.ExperienceByMember["CREW_05"] > 1.5 * best.ExperienceByMember["CREW_05"]);
        Assert.True(develop.ExperienceByMember["CREW_03"] < best.ExperienceByMember["CREW_03"]);
    }

    private static BenchFigures Run(int lead, int[] team, Parameters parameters)
    {
        return Bench.Run(PrototypeCrew.Pack, PrototypeCrew.Pack.Tuning, new BenchOptions("OBJECTIVE_TEMPLATE_1")
        {
            Runs = 1000,
            Team = PrototypeCrew.Team(lead, team),
            GearTags = PrototypeCrew.Gear,
            Parameters = parameters,
        });
    }

    private static double Calls(BenchFigures figures)
    {
        return (double)figures.Calls / figures.Runs;
    }
}

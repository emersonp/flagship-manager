using SimRunner;

namespace SimRunner.Tests;

public class SimulationTests
{
    [Fact]
    public void An_empty_cycle_run_produces_two_founding_events_and_seven_per_cycle()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 3, Cycles: 10, Seed: 1));

        Assert.Equal(new ulong[] { 1, 2, 3 }, summary.Legacies.Select(l => l.Seed));
        Assert.All(summary.Legacies, l => Assert.Equal(72, l.Events));
    }

    // Golden seed: this hash changes only when behaviour changes. If the change is intended, update it in the same commit and say why.
    [Fact]
    public void Golden_seed_1_matches_the_accepted_log()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 1, Cycles: 10, Seed: 1));

        Assert.Equal("B0AD902EB4679031544F1B88FD84DE1702B01E2EC74821F6A46F40E77FB51D7E", summary.Legacies[0].LogHash);
    }
}

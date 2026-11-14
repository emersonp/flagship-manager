using SimRunner;

namespace SimRunner.Tests;

public class SimulationTests
{
    [Fact]
    public void An_empty_cycle_run_produces_six_events_per_cycle()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 3, Cycles: 10, Seed: 1));

        Assert.Equal(new ulong[] { 1, 2, 3 }, summary.Legacies.Select(l => l.Seed));
        Assert.All(summary.Legacies, l => Assert.Equal(60, l.Events));
    }

    // Golden seed: this hash changes only when behaviour changes. If the change is intended, update it in the same commit and say why.
    [Fact]
    public void Golden_seed_1_matches_the_accepted_log()
    {
        SimSummary summary = Simulation.Run(new SimOptions(Legacies: 1, Cycles: 10, Seed: 1));

        Assert.Equal("0407DFACDF8847EB7B1571D8DA27C0A3705A5A45F7DF82BE06A23D22FBCEF520", summary.Legacies[0].LogHash);
    }
}

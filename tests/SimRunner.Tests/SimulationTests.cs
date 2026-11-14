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

        Assert.Equal("13EF18B7B8B101CD31DDCAC9366780EA947CAAB8268EC493FADC2E3766F3DD49", summary.Legacies[0].LogHash);
    }
}

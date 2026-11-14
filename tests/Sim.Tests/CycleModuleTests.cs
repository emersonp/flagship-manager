using Sim.Core;
using Sim.Cycle;

namespace Sim.Tests;

public class CycleModuleTests
{
    [Fact]
    public void Five_advances_walk_the_phases_and_start_the_next_cycle()
    {
        var bus = new EventBus();
        CycleModule.Register(bus);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };
        var events = new List<IEvent>();

        for (int i = 0; i < 5; i++)
        {
            events.AddRange(bus.Send(state, new AdvancePhase()).Select(p => p.Event));
        }

        Assert.Equal(
            new IEvent[]
            {
                new PhaseEntered(1, CyclePhase.Briefing),
                new PhaseEntered(1, CyclePhase.Resolution),
                new PhaseEntered(1, CyclePhase.Aftermath),
                new PhaseEntered(1, CyclePhase.Advance),
                new CycleAdvanced(2),
                new PhaseEntered(2, CyclePhase.Orders),
            },
            events);
        Assert.Equal(2, state.Cycle);
        Assert.Equal(CyclePhase.Orders, state.Phase);
    }

    // Streams belong to one cycle; old ones are dropped so state does not grow forever.
    [Fact]
    public void Rng_streams_from_a_finished_cycle_are_dropped()
    {
        var bus = new EventBus();
        CycleModule.Register(bus);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1, Phase = CyclePhase.Advance };
        state.RngStates["1:missions"] = 99;

        bus.Send(state, new AdvancePhase());

        Assert.Empty(state.RngStates);
    }
}

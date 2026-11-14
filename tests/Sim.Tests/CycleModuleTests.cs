using System.Text.Json;
using Sim.Core;
using Sim.Cycle;

namespace Sim.Tests;

public class CycleModuleTests
{
    private static EventBus NewBus()
    {
        var bus = new EventBus();
        CycleModule.Register(bus);
        return bus;
    }

    private static LegacyState NewState()
    {
        return new LegacyState { LegacyId = Guid.Empty, Seed = 1 };
    }

    private static LegacyState FoundedState(EventBus bus)
    {
        LegacyState state = NewState();
        bus.Send(state, new FoundLegacy());
        return state;
    }

    [Fact]
    public void Founding_a_legacy_publishes_it_then_enters_the_first_phase()
    {
        EventBus bus = NewBus();

        IReadOnlyList<PublishedEvent> published = bus.Send(NewState(), new FoundLegacy());

        Assert.Equal(
            new IEvent[] { new LegacyFounded(), new PhaseEntered(1, CyclePhase.Orders) },
            published.Select(p => p.Event));
    }

    [Fact]
    public void A_second_founding_is_refused()
    {
        EventBus bus = NewBus();
        LegacyState state = FoundedState(bus);

        Assert.Throws<CommandRejectedException>(() => bus.Send(state, new FoundLegacy()));
    }

    [Fact]
    public void Advancing_before_founding_is_refused()
    {
        EventBus bus = NewBus();

        LegacyState state = NewState();
        string before = JsonSerializer.Serialize(state, SimJson.Options);

        Assert.Throws<CommandRejectedException>(() => bus.Send(state, new AdvancePhase()));

        Assert.Equal(before, JsonSerializer.Serialize(state, SimJson.Options));
    }

    [Fact]
    public void Five_advances_walk_the_phases_and_start_the_next_cycle_and_week()
    {
        EventBus bus = NewBus();
        LegacyState state = FoundedState(bus);
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
                new WeekAdvanced(2),
                new PhaseEntered(2, CyclePhase.Orders),
            },
            events);
        Assert.Equal(2, state.Cycle);
        Assert.Equal(2, state.Week);
        Assert.Equal(CyclePhase.Orders, state.Phase);
    }

    // Streams belong to one cycle; old ones are dropped so state does not grow forever.
    [Fact]
    public void Rng_streams_from_a_finished_cycle_are_dropped()
    {
        EventBus bus = NewBus();
        LegacyState state = FoundedState(bus);
        state.Phase = CyclePhase.Advance;
        state.RngStates["1:missions"] = 99;

        bus.Send(state, new AdvancePhase());

        Assert.Empty(state.RngStates);
    }
}

using System.Text.Json;
using Sim.Core;
using Sim.Cycle;

namespace Sim.Tests;

public class CycleModuleTests
{
    private static EventBus NewBus()
    {
        var bus = new EventBus();
        CycleModule.Register(bus, TestContent.Pack);
        return bus;
    }

    private static LegacyState NewState(ulong seed = 1)
    {
        return new LegacyState { LegacyId = Guid.Empty, Seed = seed };
    }

    // A founded legacy in its advance phase, ready to turn.
    private static LegacyState AtAdvance(EventBus bus, ulong seed = 1)
    {
        LegacyState state = NewState(seed);
        bus.Send(state, new FoundLegacy());
        state.Phase = CyclePhase.Advance;
        return state;
    }

    // 13 MONTH_B 105, as the game's 13 Vortun 105.
    private const int StartDay = 37482;

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
            new IEvent[] { new LegacyFounded(), new PhaseEntered(1, CyclePhase.Itinerary) },
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
    public void Six_advances_walk_the_phases_and_open_the_next_cycle_on_its_itinerary()
    {
        EventBus bus = NewBus();
        LegacyState state = FoundedState(bus);
        var events = new List<IEvent>();

        for (int i = 0; i < 6; i++)
        {
            events.AddRange(bus.Send(state, new AdvancePhase()).Select(p => p.Event));
        }

        Assert.Equal(
            new IEvent[]
            {
                new PhaseEntered(1, CyclePhase.Orders),
                new PhaseEntered(1, CyclePhase.Briefing),
                new PhaseEntered(1, CyclePhase.Resolution),
                new PhaseEntered(1, CyclePhase.Aftermath),
                new PhaseEntered(1, CyclePhase.Advance),
            },
            events.Take(5));
        var rolled = (CycleLengthRolled)events[5];
        Assert.Equal(6, rolled.PlannedDays);
        Assert.Equal(new CycleAdvanced(2), events[6]);
        Assert.Equal(Enumerable.Range(StartDay + 1, rolled.Days).Select(d => new DayReached(d)), events.Skip(7).Take(rolled.Days));
        Assert.Equal(new PhaseEntered(2, CyclePhase.Itinerary), events[^1]);
        Assert.Equal(8 + rolled.Days, events.Count);
        Assert.Equal(2, state.Cycle);
        Assert.Equal(StartDay + rolled.Days, state.Day);
        Assert.Equal(StartDay, state.StartDay);
        Assert.Equal(CyclePhase.Itinerary, state.Phase);
    }

    [Fact]
    public void Founding_sets_the_date_to_the_calendars_start()
    {
        EventBus bus = NewBus();

        LegacyState state = FoundedState(bus);

        Assert.Equal((StartDay, StartDay), (state.Day, state.StartDay));
    }

    // 1d3+4 (design 4.4): over 300 seeds every turn is 5 to 7 days, and each length is rolled.
    [Fact]
    public void A_cycles_length_is_5_to_7_days_and_each_is_rolled()
    {
        EventBus bus = NewBus();
        var lengths = new List<int>();

        for (ulong seed = 1; seed <= 300; seed++)
        {
            LegacyState state = AtAdvance(bus, seed);
            lengths.Add(bus.Send(state, new AdvancePhase()).Select(p => p.Event).OfType<CycleLengthRolled>().Single().Days);
        }

        Assert.All(lengths, l => Assert.InRange(l, 5, 7));
        Assert.Equal([5, 6, 7], lengths.Distinct().Order());
    }

    // Each day's subscribers see that day: Day is set before them.
    [Fact]
    public void Each_day_is_reached_once_in_order_with_the_date_already_moved()
    {
        EventBus bus = NewBus();
        var seen = new List<(int Event, int State)>();
        bus.Subscribe<DayReached>(500, "test", (e, context) => seen.Add((e.Day, context.State.Day)));
        LegacyState state = AtAdvance(bus);

        int days = bus.Send(state, new AdvancePhase()).Select(p => p.Event).OfType<CycleLengthRolled>().Single().Days;

        Assert.Equal(Enumerable.Range(StartDay + 1, days).Select(d => (d, d)), seen);
    }

    // One roll times N, never N rolls: 25, 30, or 35 days for five weeks, and one cycle.
    [Fact]
    public void Travel_of_five_weeks_is_one_roll_times_five_and_one_cycle()
    {
        EventBus bus = NewBus();
        var lengths = new HashSet<int>();

        for (ulong seed = 1; seed <= 60; seed++)
        {
            LegacyState state = AtAdvance(bus, seed);
            List<IEvent> events = bus.Send(state, new Travel(5)).Select(p => p.Event).ToList();
            CycleLengthRolled rolled = events.OfType<CycleLengthRolled>().Single();
            Assert.Equal(30, rolled.PlannedDays);
            Assert.Equal(rolled.Days, events.OfType<DayReached>().Count());
            Assert.Equal((2, StartDay + rolled.Days, CyclePhase.Itinerary), (state.Cycle, state.Day, state.Phase));
            lengths.Add(rolled.Days);
        }

        Assert.Equal([25, 30, 35], lengths.Order());
    }

    // The same seed rolls the same length whether the cycle turns by AdvancePhase or by one week of travel.
    [Fact]
    public void The_length_is_drawn_from_the_calendar_stream_alone()
    {
        EventBus bus = NewBus();
        LegacyState advanced = AtAdvance(bus, 7);
        LegacyState travelled = AtAdvance(bus, 7);
        int expected = 5 + RngStream.For(7, 1, CycleModule.CalendarStream).Next(3);

        bus.Send(advanced, new AdvancePhase());
        bus.Send(travelled, new Travel(1));

        Assert.Equal((StartDay + expected, StartDay + expected), (advanced.Day, travelled.Day));
    }

    [Theory]
    [InlineData(CyclePhase.Itinerary, 1)]
    [InlineData(CyclePhase.Orders, 1)]
    [InlineData(CyclePhase.Aftermath, 1)]
    [InlineData(CyclePhase.Advance, 0)]
    [InlineData(CyclePhase.Advance, 11)]
    public void Travel_is_refused_outside_the_advance_phase_or_beyond_1_to_10_weeks(CyclePhase phase, int weeks)
    {
        EventBus bus = NewBus();
        LegacyState state = AtAdvance(bus);
        state.Phase = phase;
        string before = JsonSerializer.Serialize(state, SimJson.Options);

        Assert.Throws<CommandRejectedException>(() => bus.Send(state, new Travel(weeks)));

        Assert.Equal(before, JsonSerializer.Serialize(state, SimJson.Options));
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

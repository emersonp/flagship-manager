using Sim.Core;

namespace Sim.Tests;

public class EventBusTests
{
    private sealed record Ping(string Text) : ICommand;

    private sealed record Relay : ICommand;

    private sealed record Pinged(string Text) : IEvent;

    private sealed record Echoed(string Text) : IEvent;

    private static LegacyState NewState()
    {
        return new LegacyState { LegacyId = Guid.Empty, Seed = 1 };
    }

    [Fact]
    public void A_command_with_no_handler_throws()
    {
        var bus = new EventBus();

        Assert.Throws<InvalidOperationException>(() => bus.Send(NewState(), new Ping("x")));
    }

    [Fact]
    public void A_second_handler_for_one_command_throws()
    {
        var bus = new EventBus();
        bus.Handle<Ping>((_, _) => { });

        Assert.Throws<InvalidOperationException>(() => bus.Handle<Ping>((_, _) => { }));
    }

    [Fact]
    public void A_duplicate_subscription_throws()
    {
        var bus = new EventBus();
        bus.Subscribe<Pinged>(10, "echo", (_, _) => { });

        Assert.Throws<InvalidOperationException>(() => bus.Subscribe<Pinged>(10, "echo", (_, _) => { }));
    }

    // Replays depend on this: order is by declared number, then name, never by registration order.
    [Fact]
    public void Subscribers_run_in_declared_order()
    {
        var bus = new EventBus();
        var calls = new List<string>();
        bus.Handle<Ping>((command, context) => context.Publish(new Pinged(command.Text)));
        bus.Subscribe<Pinged>(20, "b", (_, _) => calls.Add("20b"));
        bus.Subscribe<Pinged>(10, "z", (_, _) => calls.Add("10z"));
        bus.Subscribe<Pinged>(20, "a", (_, _) => calls.Add("20a"));

        bus.Send(NewState(), new Ping("x"));

        Assert.Equal(new[] { "10z", "20a", "20b" }, calls);
    }

    // Events raised by subscribers are queued behind the current event and still returned for the log.
    [Fact]
    public void Events_published_by_subscribers_are_returned_in_order_with_sequence_numbers()
    {
        var bus = new EventBus();
        bus.Handle<Ping>((command, context) =>
        {
            context.Publish(new Pinged(command.Text + "1"));
            context.Publish(new Pinged(command.Text + "2"));
        });
        bus.Subscribe<Pinged>(10, "echo", (e, context) => context.Publish(new Echoed(e.Text)));
        LegacyState state = NewState();

        IReadOnlyList<PublishedEvent> published = bus.Send(state, new Ping("x"));

        Assert.Equal(
            new IEvent[] { new Pinged("x1"), new Pinged("x2"), new Echoed("x1"), new Echoed("x2") },
            published.Select(p => p.Event));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, published.Select(p => p.Sequence));
        Assert.Equal(5, state.NextSequence);
    }

    [Fact]
    public void A_handler_can_send_another_command()
    {
        var bus = new EventBus();
        bus.Handle<Ping>((command, context) => context.Publish(new Pinged(command.Text)));
        bus.Handle<Relay>((_, context) => context.Send(new Ping("relayed")));

        IReadOnlyList<PublishedEvent> published = bus.Send(NewState(), new Relay());

        Assert.Equal(new Pinged("relayed"), Assert.Single(published).Event);
    }

    // A stream must continue across commands within a cycle, not restart.
    [Fact]
    public void Rng_streams_continue_across_commands_and_are_saved_in_state()
    {
        var bus = new EventBus();
        var draws = new List<ulong>();
        bus.Handle<Ping>((_, context) => draws.Add(context.Rng("test").NextUInt64()));
        LegacyState state = NewState();

        bus.Send(state, new Ping("a"));
        bus.Send(state, new Ping("b"));

        RngStream expected = RngStream.For(1, 1, "test");
        Assert.Equal(new[] { expected.NextUInt64(), expected.NextUInt64() }, draws);
        Assert.Equal(expected.State, state.RngStates["1:test"]);
    }
}

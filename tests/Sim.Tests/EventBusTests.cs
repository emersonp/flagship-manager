using System.Text.Json;
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

    [Fact]
    public void Guards_run_in_declared_order_before_the_handler()
    {
        var bus = new EventBus();
        var calls = new List<string>();
        bus.Handle<Ping>((_, _) => calls.Add("handler"));
        bus.Guard<Ping>(20, "b", (_, _) => { calls.Add("20b"); return null; });
        bus.Guard<Ping>(10, "z", (_, _) => { calls.Add("10z"); return null; });
        bus.Guard<Ping>(20, "a", (_, _) => { calls.Add("20a"); return null; });

        bus.Send(NewState(), new Ping("x"));

        Assert.Equal(new[] { "10z", "20a", "20b", "handler" }, calls);
    }

    [Fact]
    public void A_refusing_guard_throws_its_reason_and_the_handler_does_not_run()
    {
        var bus = new EventBus();
        bool ran = false;
        bool laterGuardRan = false;
        bus.Handle<Ping>((_, _) => ran = true);
        bus.Guard<Ping>(10, "no", (_, _) => "not today");
        bus.Guard<Ping>(20, "later", (_, _) => { laterGuardRan = true; return null; });

        var refused = Assert.Throws<CommandRejectedException>(() => bus.Send(NewState(), new Ping("x")));

        Assert.Equal("not today", refused.Message);
        Assert.False(ran);
        Assert.False(laterGuardRan);
    }

    // A top-level refusal must leave the state byte-for-byte as it was, so the caller can keep using it.
    [Fact]
    public void A_refused_top_level_command_leaves_the_state_unchanged()
    {
        var bus = new EventBus();
        bus.Handle<Ping>((command, context) => context.Publish(new Pinged(command.Text)));
        bus.Guard<Ping>(10, "rng", (_, context) =>
        {
            context.Rng("test").NextUInt64();
            return "no";
        });
        LegacyState state = NewState();
        state.NextSequence = 5;
        state.RngStates["1:test"] = 7;
        string before = JsonSerializer.Serialize(state, SimJson.Options);

        Assert.Throws<CommandRejectedException>(() => bus.Send(state, new Ping("x")));

        Assert.Equal(before, JsonSerializer.Serialize(state, SimJson.Options));
    }

    [Fact]
    public void A_nested_send_is_guarded_too()
    {
        var bus = new EventBus();
        bool ran = false;
        bus.Handle<Ping>((_, _) => ran = true);
        bus.Handle<Relay>((_, context) => context.Send(new Ping("relayed")));
        bus.Guard<Ping>(10, "no", (_, _) => "nested refusal");

        var refused = Assert.Throws<CommandRejectedException>(() => bus.Send(NewState(), new Relay()));

        Assert.Equal("nested refusal", refused.Message);
        Assert.False(ran);
    }

    [Fact]
    public void A_duplicate_guard_throws()
    {
        var bus = new EventBus();
        bus.Guard<Ping>(10, "g", (_, _) => null);

        Assert.Throws<InvalidOperationException>(() => bus.Guard<Ping>(10, "g", (_, _) => null));
    }

    private sealed class Counter
    {
        public int Count { get; set; }
    }

    private sealed class Other
    {
        public int Count { get; set; }
    }

    private interface ICountView
    {
        int Count { get; }
    }

    private sealed record Bump : ICommand;

    private sealed class CountView(Counter counter) : ICountView
    {
        public int Count => counter.Count;
    }

    [Fact]
    public void A_module_state_is_created_once_and_returned_again()
    {
        var bus = new EventBus();
        bus.Module<Counter>("counter");
        var seen = new List<Counter>();
        bus.Handle<Bump>((_, context) => seen.Add(context.Module<Counter>()));
        LegacyState state = NewState();

        bus.Send(state, new Bump());
        bus.Send(state, new Bump());

        Assert.Same(seen[0], seen[1]);
        Assert.Same(seen[0], state.Modules["counter"]);
    }

    [Fact]
    public void Two_modules_keep_separate_state()
    {
        var bus = new EventBus();
        bus.Module<Counter>("counter");
        bus.Module<Other>("other");
        object? first = null;
        object? second = null;
        bus.Handle<Bump>((_, context) =>
        {
            first = context.Module<Counter>();
            second = context.Module<Other>();
        });

        bus.Send(NewState(), new Bump());

        Assert.NotSame(first, second);
    }

    [Fact]
    public void An_undeclared_module_type_throws()
    {
        var bus = new EventBus();
        bus.Handle<Bump>((_, context) => context.Module<Counter>());

        Assert.Throws<InvalidOperationException>(() => bus.Send(NewState(), new Bump()));
    }

    [Fact]
    public void A_duplicate_module_name_or_type_throws()
    {
        var bus = new EventBus();
        bus.Module<Counter>("counter");

        Assert.Throws<InvalidOperationException>(() => bus.Module<Other>("counter"));
        Assert.Throws<InvalidOperationException>(() => bus.Module<Counter>("again"));
        Assert.Equal(typeof(Counter), Assert.Single(bus.ModuleTypes).Value);
    }

    [Fact]
    public void A_view_reflects_its_modules_state_after_a_command_changes_it()
    {
        var bus = new EventBus();
        bus.Module<Counter>("counter");
        bus.Provide<ICountView>(state => new CountView(state.Module<Counter>("counter")));
        bus.Handle<Bump>((_, context) => context.Module<Counter>().Count++);
        int inside = -1;
        bus.Handle<Relay>((_, context) =>
        {
            context.Send(new Bump());
            inside = context.View<ICountView>().Count;
        });
        LegacyState state = NewState();

        bus.Send(state, new Relay());

        Assert.Equal(1, inside);
        Assert.Equal(1, bus.View<ICountView>(state).Count);
    }

    [Fact]
    public void An_unprovided_view_throws_and_providing_one_twice_throws()
    {
        var bus = new EventBus();

        Assert.Throws<InvalidOperationException>(() => bus.View<ICountView>(NewState()));

        bus.Provide<ICountView>(state => new CountView(state.Module<Counter>("counter")));

        Assert.Throws<InvalidOperationException>(() => bus.Provide<ICountView>(state => new CountView(state.Module<Counter>("counter"))));
    }
}

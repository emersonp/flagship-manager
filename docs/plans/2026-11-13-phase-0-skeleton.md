# Phase 0 Skeleton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the solution, the pure simulation core, the event log, the content loader, and a headless `SimRunner` that plays empty cycles deterministically.

**Architecture:** Pure `Sim.*` class libraries hold all rules: state in, commands and events out. An in-process, ordered event bus carries commands (one handler each) and events (any number of subscribers, every one logged). `Persistence` (SQLite) and `SimRunner` (console) sit outside the pure core.

**Tech Stack:** .NET 10, C#, xUnit 2, YamlDotNet 18.1.0, Microsoft.Data.Sqlite 11.0.0, System.Text.Json.

**Spec:** `docs/gdd.md` (sections 7, 10, and 11), with vocabulary from `GLOSSARY.md`.

**Verified:** every file in this plan was compiled and every test run on 2026-11-13 (.NET SDK 10.0.402, 34 tests passing). The pinned constants in Tasks 2 and 8 come from that run. If a pinned value does not match, the code has drifted from this plan; fix the code, not the constant.

## Global Constraints

- Target framework `net10.0`; nullable enabled; warnings are errors.
- Every `if`, `else`, `for`, `foreach`, and `while` body is in braces on their own lines, even one-liners (enforced by `.editorconfig`, rule IDE0011).
- Comments are terse and explain why, never what. Each test has a one-line intent comment only where the name does not say it.
- Everything under `src/Sim.*` is pure: no database, no clock, and no ambient randomness. Randomness comes only from `IBusContext.Rng(streamName)`.
- `Sim.*` projects reference only `Sim.Core`. Feature modules never reference each other.
- Events are versioned and additive: a field is never removed or repurposed; a changed meaning is a new event type or version.
- Use the terms in `GLOSSARY.md` (legacy, cycle, chair, captain, and so on). Never "save", "turn", or "campaign".
- Creative content is the author's. Use obvious placeholders (`SKILL_A`, `CREW_01`); never invent names, species, or story text.
- Commit messages are plain: no co-author trailer and no tool attribution of any kind.
- Prose uses the Oxford comma.
- On this machine the SDK is at `~/.dotnet`. Run `export PATH=$HOME/.dotnet:$PATH` first.
- All commands run from the repository root, `~/repos/starship-sim`.

## File Structure

| Path | Responsibility |
|---|---|
| `Directory.Build.props`, `.editorconfig`, `.gitignore` | Build settings shared by every project |
| `src/Sim.Core/RngStream.cs` | Seeded, named, restorable random streams |
| `src/Sim.Core/LegacyState.cs` | The state of one legacy that Phase 0 needs, and `CyclePhase` |
| `src/Sim.Core/Messages.cs` | `ICommand`, `IEvent`, `[EventType]`, `PublishedEvent`, and `IBusContext` |
| `src/Sim.Core/EventBus.cs` | Command dispatch, ordered event delivery, and stream bookkeeping |
| `src/Sim.Core/EventLog.cs` | `EventEnvelope` and the registry that turns events into envelopes and back |
| `src/Sim.Cycle/CycleModule.cs` | The phase state machine |
| `src/Sim.Content/ContentPack.cs`, `ContentLoader.cs` | Content pack model, YAML loading, and validation |
| `src/Persistence/ILegacyStore.cs`, `SqliteLegacyStore.cs`, `LegacyExport.cs` | Legacy rows, the append-only event log, and developer-mode export and import |
| `src/SimRunner/Simulation.cs`, `Program.cs` | Headless runs and the command line |
| `content/` | The content pack (placeholders until the author writes it) |
| `tests/Sim.Tests`, `tests/Persistence.Tests`, `tests/SimRunner.Tests` | Tests, including the architecture rules |
| `check.sh` | The gate every change must pass |

---

### Task 1: Solution skeleton

**Files:**
- Create: `Directory.Build.props`, `.editorconfig`, `.gitignore`
- Create: `StarshipSim.slnx` and eight projects under `src/` and `tests/`

**Interfaces:**
- Consumes: nothing.
- Produces: projects `Sim.Core`, `Sim.Content`, `Sim.Cycle`, `Persistence`, `SimRunner`, `Sim.Tests`, `Persistence.Tests`, and `SimRunner.Tests`, with references and packages in place.

- [ ] **Step 1: Write the shared build files**

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

`.editorconfig`:

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
insert_final_newline = true

[*.{csproj,props,yaml,yml,json}]
indent_size = 2

[*.cs]
csharp_new_line_before_open_brace = all
csharp_prefer_braces = true:error
dotnet_diagnostic.IDE0011.severity = error
```

`.gitignore`:

```gitignore
bin/
obj/
*.db
*.db-shm
*.db-wal
out/
.vs/
*.user
```


- [ ] **Step 2: Create the solution and projects**

```bash
dotnet new sln -n StarshipSim
for p in Sim.Core Sim.Content Sim.Cycle Persistence; do dotnet new classlib -n $p -o src/$p; rm src/$p/Class1.cs; done
dotnet new console -n SimRunner -o src/SimRunner
for t in Sim.Tests Persistence.Tests SimRunner.Tests; do dotnet new xunit -n $t -o tests/$t; rm tests/$t/UnitTest1.cs; done
dotnet sln add src/*/*.csproj tests/*/*.csproj
```

- [ ] **Step 3: Add references and packages**

```bash
dotnet add src/Sim.Content reference src/Sim.Core
dotnet add src/Sim.Cycle reference src/Sim.Core
dotnet add src/Persistence reference src/Sim.Core
dotnet add src/SimRunner reference src/Sim.Core src/Sim.Cycle src/Sim.Content
dotnet add src/Sim.Content package YamlDotNet --version 18.1.0
dotnet add src/Persistence package Microsoft.Data.Sqlite --version 11.0.0
dotnet add tests/Sim.Tests reference src/Sim.Core src/Sim.Cycle src/Sim.Content
dotnet add tests/Persistence.Tests reference src/Persistence src/Sim.Core
dotnet add tests/SimRunner.Tests reference src/SimRunner
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `Build succeeded` with 0 warnings and 0 errors.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add solution skeleton"
```

---

### Task 2: Seeded random streams

**Files:**
- Create: `src/Sim.Core/RngStream.cs`
- Test: `tests/Sim.Tests/RngStreamTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `RngStream.For(ulong legacySeed, int cycle, string streamName)`, `new RngStream(ulong state)`, `ulong State`, `NextUInt64()`, `NextDouble()`, `Next(int maxExclusive)`, `D6()`, and `TwoD6()`.

- [ ] **Step 1: Write the failing tests**

`tests/Sim.Tests/RngStreamTests.cs`:

```csharp
using Sim.Core;

namespace Sim.Tests;

public class RngStreamTests
{
    // Same seed, cycle, and name must always give the same draws.
    [Fact]
    public void Same_inputs_give_the_same_sequence()
    {
        RngStream a = RngStream.For(42, 3, "missions");
        RngStream b = RngStream.For(42, 3, "missions");

        Assert.Equal(Draw(a, 20), Draw(b, 20));
    }

    // A new mechanic drawing from its own stream must not disturb existing streams.
    [Theory]
    [InlineData(42UL, 3, "storylets")]
    [InlineData(42UL, 4, "missions")]
    [InlineData(43UL, 3, "missions")]
    public void Different_inputs_give_different_sequences(ulong seed, int cycle, string name)
    {
        RngStream baseline = RngStream.For(42, 3, "missions");
        RngStream other = RngStream.For(seed, cycle, name);

        Assert.NotEqual(Draw(baseline, 20), Draw(other, 20));
    }

    // A stream restored from its saved state continues where it stopped.
    [Fact]
    public void Restoring_state_continues_the_sequence()
    {
        RngStream original = RngStream.For(7, 1, "missions");
        Draw(original, 5);
        var restored = new RngStream(original.State);

        Assert.Equal(Draw(original, 10), Draw(restored, 10));
    }

    // Guards the algorithm itself: changing it would silently change every legacy.
    [Fact]
    public void Algorithm_is_pinned()
    {
        RngStream stream = RngStream.For(1, 1, "golden");

        Assert.Equal(new ulong[] { 5517430893459967052UL, 16803158252055916467UL, 1249752661054523924UL }, Draw(stream, 3));
    }

    [Fact]
    public void Dice_stay_in_range()
    {
        RngStream stream = RngStream.For(1, 1, "dice");
        for (int i = 0; i < 1000; i++)
        {
            Assert.InRange(stream.D6(), 1, 6);
            Assert.InRange(stream.TwoD6(), 2, 12);
            Assert.InRange(stream.Next(10), 0, 9);
        }
    }

    private static ulong[] Draw(RngStream stream, int count)
    {
        return Enumerable.Range(0, count).Select(_ => stream.NextUInt64()).ToArray();
    }
}
```


- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sim.Tests`
Expected: build error, `RngStream` does not exist.

- [ ] **Step 3: Implement**

`src/Sim.Core/RngStream.cs`:

```csharp
using System.Text;

namespace Sim.Core;

public sealed class RngStream
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    public RngStream(ulong state)
    {
        State = state;
    }

    public ulong State { get; private set; }

    public static RngStream For(ulong legacySeed, int cycle, string streamName)
    {
        ulong nameHash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(streamName))
        {
            nameHash ^= b;
            nameHash *= 1099511628211UL;
        }

        ulong state = Mix(legacySeed + Golden);
        state = Mix(state ^ (ulong)cycle);
        state = Mix(state ^ nameHash);
        return new RngStream(state);
    }

    public ulong NextUInt64()
    {
        State += Golden;
        return Mix(State);
    }

    public double NextDouble()
    {
        return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        return (int)(NextDouble() * maxExclusive);
    }

    public int D6()
    {
        return 1 + Next(6);
    }

    public int TwoD6()
    {
        return D6() + D6();
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
```


- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Sim.Tests`
Expected: 5 test methods pass (7 cases).

- [ ] **Step 5: Commit**

```bash
git add src/Sim.Core tests/Sim.Tests
git commit -m "Add seeded random streams"
```

---

### Task 3: Event bus

**Files:**
- Create: `src/Sim.Core/LegacyState.cs`, `src/Sim.Core/Messages.cs`, `src/Sim.Core/EventBus.cs`
- Test: `tests/Sim.Tests/EventBusTests.cs`

**Interfaces:**
- Consumes: `RngStream` from Task 2.
- Produces: `LegacyState { Guid LegacyId; ulong Seed; int Cycle; CyclePhase Phase; long NextSequence; Dictionary<string, ulong> RngStates }`; `enum CyclePhase { Orders, Briefing, Resolution, Aftermath, Advance }`; `ICommand`; `IEvent`; `EventTypeAttribute(string name, int version)`; `PublishedEvent(long Sequence, int Cycle, IEvent Event)`; `IBusContext { State; Publish(IEvent); Send(ICommand); Rng(string) }`; `EventBus.Handle<TCommand>(Action<TCommand, IBusContext>)`; `EventBus.Subscribe<TEvent>(int order, string name, Action<TEvent, IBusContext>)`; `EventBus.Send(LegacyState, ICommand) -> IReadOnlyList<PublishedEvent>`.

- [ ] **Step 1: Write the state and message types**

`src/Sim.Core/LegacyState.cs`:

```csharp
namespace Sim.Core;

public enum CyclePhase
{
    Orders,
    Briefing,
    Resolution,
    Aftermath,
    Advance,
}

public sealed class LegacyState
{
    public required Guid LegacyId { get; init; }

    public required ulong Seed { get; init; }

    public int Cycle { get; set; } = 1;

    public CyclePhase Phase { get; set; } = CyclePhase.Orders;

    public long NextSequence { get; set; } = 1;

    // Keyed "cycle:streamName". Only the current cycle's streams are kept.
    public Dictionary<string, ulong> RngStates { get; init; } = new();
}
```

`src/Sim.Core/Messages.cs`:

```csharp
namespace Sim.Core;

public interface ICommand
{
}

public interface IEvent
{
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventTypeAttribute(string name, int version) : Attribute
{
    public string Name { get; } = name;

    public int Version { get; } = version;
}

public sealed record PublishedEvent(long Sequence, int Cycle, IEvent Event);

public interface IBusContext
{
    LegacyState State { get; }

    void Publish(IEvent @event);

    void Send(ICommand command);

    RngStream Rng(string streamName);
}
```


- [ ] **Step 2: Write the failing tests**

`tests/Sim.Tests/EventBusTests.cs`:

```csharp
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
```


- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/Sim.Tests`
Expected: build error, `EventBus` does not exist.

- [ ] **Step 4: Implement**

`src/Sim.Core/EventBus.cs`:

```csharp
namespace Sim.Core;

public sealed class EventBus
{
    private readonly Dictionary<Type, Action<ICommand, IBusContext>> handlers = new();
    private readonly List<Subscription> subscriptions = new();

    public void Handle<TCommand>(Action<TCommand, IBusContext> handler)
        where TCommand : ICommand
    {
        if (!handlers.TryAdd(typeof(TCommand), (command, context) => handler((TCommand)command, context)))
        {
            throw new InvalidOperationException($"Command {typeof(TCommand).Name} already has a handler.");
        }
    }

    public void Subscribe<TEvent>(int order, string name, Action<TEvent, IBusContext> subscriber)
        where TEvent : IEvent
    {
        subscriptions.Add(new Subscription(typeof(TEvent), order, name, (e, context) => subscriber((TEvent)e, context)));
        subscriptions.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
    }

    public IReadOnlyList<PublishedEvent> Send(LegacyState state, ICommand command)
    {
        var context = new Context(this, state);
        context.Send(command);
        while (context.Pending.Count > 0)
        {
            PublishedEvent published = context.Pending.Dequeue();
            foreach (Subscription subscription in subscriptions)
            {
                if (subscription.EventType == published.Event.GetType())
                {
                    subscription.Invoke(published.Event, context);
                }
            }
        }

        context.SaveStreams();
        return context.Published;
    }

    private sealed record Subscription(Type EventType, int Order, string Name, Action<IEvent, IBusContext> Invoke);

    private sealed class Context(EventBus bus, LegacyState state) : IBusContext
    {
        private readonly Dictionary<string, RngStream> streams = new();

        public LegacyState State { get; } = state;

        public Queue<PublishedEvent> Pending { get; } = new();

        public List<PublishedEvent> Published { get; } = new();

        public void Publish(IEvent @event)
        {
            var published = new PublishedEvent(State.NextSequence++, State.Cycle, @event);
            Published.Add(published);
            Pending.Enqueue(published);
        }

        public void Send(ICommand command)
        {
            if (!bus.handlers.TryGetValue(command.GetType(), out Action<ICommand, IBusContext>? handler))
            {
                throw new InvalidOperationException($"Command {command.GetType().Name} has no handler.");
            }

            handler(command, this);
        }

        public RngStream Rng(string streamName)
        {
            string key = $"{State.Cycle}:{streamName}";
            if (!streams.TryGetValue(key, out RngStream? stream))
            {
                stream = State.RngStates.TryGetValue(key, out ulong saved)
                    ? new RngStream(saved)
                    : RngStream.For(State.Seed, State.Cycle, streamName);
                streams[key] = stream;
            }

            return stream;
        }

        public void SaveStreams()
        {
            string prefix = $"{State.Cycle}:";
            foreach (string stale in State.RngStates.Keys.Where(k => !k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                State.RngStates.Remove(stale);
            }

            foreach ((string key, RngStream stream) in streams)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    State.RngStates[key] = stream.State;
                }
            }
        }
    }
}
```


- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Sim.Tests`
Expected: all pass, including 6 new bus tests.

- [ ] **Step 6: Commit**

```bash
git add src/Sim.Core tests/Sim.Tests
git commit -m "Add event bus"
```

---

### Task 4: Cycle phase machine

**Files:**
- Create: `src/Sim.Cycle/CycleModule.cs`
- Test: `tests/Sim.Tests/CycleModuleTests.cs`

**Interfaces:**
- Consumes: `EventBus`, `IBusContext`, `LegacyState`, `CyclePhase`, and `[EventType]` from Task 3.
- Produces: command `AdvancePhase`; events `PhaseEntered(int Cycle, CyclePhase Phase)` and `CycleAdvanced(int Cycle)`; `CycleModule.Register(EventBus bus)`.

- [ ] **Step 1: Write the failing tests**

`tests/Sim.Tests/CycleModuleTests.cs`:

```csharp
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
```


- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sim.Tests`
Expected: build error, `CycleModule` does not exist.

- [ ] **Step 3: Implement**

`src/Sim.Cycle/CycleModule.cs`:

```csharp
using Sim.Core;

namespace Sim.Cycle;

public sealed record AdvancePhase : ICommand;

[EventType("PhaseEntered", 1)]
public sealed record PhaseEntered(int Cycle, CyclePhase Phase) : IEvent;

[EventType("CycleAdvanced", 1)]
public sealed record CycleAdvanced(int Cycle) : IEvent;

public static class CycleModule
{
    public static void Register(EventBus bus)
    {
        bus.Handle<AdvancePhase>(OnAdvancePhase);
    }

    private static void OnAdvancePhase(AdvancePhase command, IBusContext context)
    {
        LegacyState state = context.State;
        if (state.Phase == CyclePhase.Advance)
        {
            state.Cycle++;
            state.Phase = CyclePhase.Orders;
            context.Publish(new CycleAdvanced(state.Cycle));
        }
        else
        {
            state.Phase++;
        }

        context.Publish(new PhaseEntered(state.Cycle, state.Phase));
    }
}
```


- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Sim.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Sim.Cycle tests/Sim.Tests
git commit -m "Add cycle phase machine"
```

---

### Task 5: Event envelopes and type registry

**Files:**
- Create: `src/Sim.Core/EventLog.cs`
- Test: `tests/Sim.Tests/EventLogTests.cs`

**Interfaces:**
- Consumes: `PublishedEvent`, `IEvent`, and `EventTypeAttribute` from Task 3; `PhaseEntered` from Task 4 (in the test).
- Produces: `EventEnvelope(long Sequence, int Cycle, string Type, int Version, string Payload)`; `EventTypeRegistry.FromAssemblies(params Assembly[])`; `ToEnvelope(PublishedEvent) -> EventEnvelope`; `FromEnvelope(EventEnvelope) -> IEvent`.

- [ ] **Step 1: Write the failing tests**

`tests/Sim.Tests/EventLogTests.cs`:

```csharp
using Sim.Core;
using Sim.Cycle;

namespace Sim.Tests;

public class EventLogTests
{
    private readonly EventTypeRegistry registry = EventTypeRegistry.FromAssemblies(typeof(CycleModule).Assembly);

    [Fact]
    public void An_event_round_trips_through_its_envelope()
    {
        var published = new PublishedEvent(7, 3, new PhaseEntered(3, CyclePhase.Resolution));

        EventEnvelope envelope = registry.ToEnvelope(published);

        Assert.Equal(new EventEnvelope(7, 3, "PhaseEntered", 1, """{"Cycle":3,"Phase":2}"""), envelope);
        Assert.Equal(published.Event, registry.FromEnvelope(envelope));
    }

    [Fact]
    public void An_unknown_event_type_throws()
    {
        var envelope = new EventEnvelope(1, 1, "Nope", 1, "{}");

        Assert.Throws<InvalidOperationException>(() => registry.FromEnvelope(envelope));
    }

    // Every event must carry a stable name and version, because class names will change over the years.
    [Fact]
    public void An_event_without_the_attribute_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => EventTypeRegistry.FromAssemblies(typeof(EventLogTests).Assembly));
    }

    private sealed record Unlabelled : IEvent;
}
```


- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/Sim.Tests`
Expected: build error, `EventTypeRegistry` does not exist.

- [ ] **Step 3: Implement**

`src/Sim.Core/EventLog.cs`:

```csharp
using System.Reflection;
using System.Text.Json;

namespace Sim.Core;

public sealed record EventEnvelope(long Sequence, int Cycle, string Type, int Version, string Payload);

public sealed class EventTypeRegistry
{
    private readonly Dictionary<(string Name, int Version), Type> byName = new();
    private readonly Dictionary<Type, (string Name, int Version)> byType = new();

    public static EventTypeRegistry FromAssemblies(params Assembly[] assemblies)
    {
        var registry = new EventTypeRegistry();
        foreach (Type type in assemblies.SelectMany(a => a.GetTypes()).Where(t => typeof(IEvent).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract))
        {
            EventTypeAttribute attribute = type.GetCustomAttribute<EventTypeAttribute>()
                ?? throw new InvalidOperationException($"Event {type.Name} has no [EventType] attribute.");
            if (!registry.byName.TryAdd((attribute.Name, attribute.Version), type))
            {
                throw new InvalidOperationException($"Event type {attribute.Name} v{attribute.Version} is declared twice.");
            }

            registry.byType[type] = (attribute.Name, attribute.Version);
        }

        return registry;
    }

    public EventEnvelope ToEnvelope(PublishedEvent published)
    {
        Type type = published.Event.GetType();
        if (!byType.TryGetValue(type, out (string Name, int Version) key))
        {
            throw new InvalidOperationException($"Event {type.Name} is not registered.");
        }

        return new EventEnvelope(published.Sequence, published.Cycle, key.Name, key.Version, JsonSerializer.Serialize(published.Event, type));
    }

    public IEvent FromEnvelope(EventEnvelope envelope)
    {
        if (!byName.TryGetValue((envelope.Type, envelope.Version), out Type? type))
        {
            throw new InvalidOperationException($"Unknown event type {envelope.Type} v{envelope.Version}.");
        }

        return (IEvent)JsonSerializer.Deserialize(envelope.Payload, type)!;
    }
}
```


- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/Sim.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Sim.Core tests/Sim.Tests
git commit -m "Add event envelopes and type registry"
```

---

### Task 6: Content loader and validator

**Files:**
- Create: `src/Sim.Content/ContentPack.cs`, `src/Sim.Content/ContentLoader.cs`
- Create: `content/pack.yaml`, `content/skills/base.yaml`
- Test: `tests/Sim.Tests/ContentLoaderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `SkillDef(string Id, IReadOnlyList<string> Tags)`; `ContentPack(string Id, int Version, IReadOnlyList<SkillDef> Skills)`; `ContentError(string File, string Message)`; `ContentLoadResult(ContentPack? Pack, IReadOnlyList<ContentError> Errors)`; `ContentLoader.Load(IReadOnlyDictionary<string, string> files)`; `ContentLoader.LoadDirectory(string root)`.
- To add a content type later: add its folder to `KnownFolders`, a DTO, a record on `ContentPack`, and a branch on the folder name in the loop.

- [ ] **Step 1: Write the model**

`src/Sim.Content/ContentPack.cs`:

```csharp
namespace Sim.Content;

public sealed record SkillDef(string Id, IReadOnlyList<string> Tags);

public sealed record ContentPack(string Id, int Version, IReadOnlyList<SkillDef> Skills);

public sealed record ContentError(string File, string Message);

public sealed record ContentLoadResult(ContentPack? Pack, IReadOnlyList<ContentError> Errors);
```


- [ ] **Step 2: Write the failing tests**

`tests/Sim.Tests/ContentLoaderTests.cs`:

```csharp
using Sim.Content;

namespace Sim.Tests;

public class ContentLoaderTests
{
    private const string Manifest = "id: CORE\nversion: 1\n";

    [Fact]
    public void A_valid_pack_loads()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = Manifest,
            ["skills/base.yaml"] = "- id: SKILL_A\n  tags: [technical]\n- id: SKILL_B\n",
        });

        Assert.Empty(result.Errors);
        Assert.Equal("CORE", result.Pack!.Id);
        Assert.Equal(1, result.Pack.Version);
        Assert.Equal(new[] { "SKILL_A", "SKILL_B" }, result.Pack.Skills.Select(s => s.Id));
        Assert.Equal(new[] { "technical" }, result.Pack.Skills[0].Tags);
    }

    // The author fixes content in batches, so every problem is reported at once and no pack is returned.
    [Fact]
    public void All_errors_are_reported_together()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>
        {
            ["pack.yaml"] = "id: core\nversion: 0\n",
            ["skills/a.yaml"] = "- id: SKILL_A\n- id: skill b\n",
            ["skills/b.yaml"] = "- id: SKILL_A\n",
            ["skils/c.yaml"] = "- id: SKILL_C\n",
            ["skills/d.yaml"] = "- id: SKILL_D\n  tag: [typo]\n",
        });

        Assert.Null(result.Pack);
        Assert.Equal(
            new[]
            {
                "pack.yaml|Id 'core' must be upper case letters, digits, and underscores, starting with a letter.",
                "pack.yaml|version must be 1 or more.",
                "skills/a.yaml|Id 'skill b' must be upper case letters, digits, and underscores, starting with a letter.",
                "skills/b.yaml|Duplicate id 'SKILL_A', first defined in skills/a.yaml.",
                "skills/d.yaml|Line 2: Property 'tag' not found on type 'Sim.Content.ContentLoader+SkillDto'.",
                "skils/c.yaml|Unknown content folder 'skils'.",
            },
            result.Errors.Select(e => $"{e.File}|{e.Message}"));
    }

    [Fact]
    public void A_missing_manifest_is_an_error()
    {
        ContentLoadResult result = ContentLoader.Load(new Dictionary<string, string>());

        Assert.Equal("The pack has no pack.yaml.", Assert.Single(result.Errors).Message);
    }
}
```


- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/Sim.Tests`
Expected: build error, `ContentLoader` does not exist.

- [ ] **Step 4: Implement**

`src/Sim.Content/ContentLoader.cs`:

```csharp
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sim.Content;

public static partial class ContentLoader
{
    private const string ManifestFile = "pack.yaml";
    private static readonly string[] KnownFolders = ["skills"];

    // Keys are paths relative to the pack root, with forward slashes.
    public static ContentLoadResult Load(IReadOnlyDictionary<string, string> files)
    {
        var errors = new List<ContentError>();
        IDeserializer yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

        PackDto? manifest = null;
        if (files.TryGetValue(ManifestFile, out string? manifestText))
        {
            manifest = Parse<PackDto>(yaml, ManifestFile, manifestText, errors);
            if (manifest is not null)
            {
                CheckId(ManifestFile, manifest.Id, errors);
                if (manifest.Version < 1)
                {
                    errors.Add(new ContentError(ManifestFile, "version must be 1 or more."));
                }
            }
        }
        else
        {
            errors.Add(new ContentError(ManifestFile, "The pack has no pack.yaml."));
        }

        var skills = new List<SkillDef>();
        var seenIn = new Dictionary<string, string>();
        foreach ((string path, string text) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (path == ManifestFile)
            {
                continue;
            }

            string folder = path.Contains('/') ? path[..path.IndexOf('/')] : "";
            if (!KnownFolders.Contains(folder))
            {
                errors.Add(new ContentError(path, $"Unknown content folder '{folder}'."));
                continue;
            }

            foreach (SkillDto dto in Parse<List<SkillDto>>(yaml, path, text, errors) ?? [])
            {
                if (!CheckId(path, dto.Id, errors))
                {
                    continue;
                }

                if (seenIn.TryGetValue(dto.Id, out string? first))
                {
                    errors.Add(new ContentError(path, $"Duplicate id '{dto.Id}', first defined in {first}."));
                    continue;
                }

                seenIn[dto.Id] = path;
                skills.Add(new SkillDef(dto.Id, dto.Tags));
            }
        }

        ContentPack? pack = errors.Count == 0 ? new ContentPack(manifest!.Id!, manifest.Version, skills) : null;
        return new ContentLoadResult(pack, errors);
    }

    public static ContentLoadResult LoadDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            return new ContentLoadResult(null, [new ContentError(root, "Content folder not found.")]);
        }

        Dictionary<string, string> files = Directory
            .EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText);
        return Load(files);
    }

    private static T? Parse<T>(IDeserializer yaml, string file, string text, List<ContentError> errors)
        where T : class
    {
        try
        {
            T? value = yaml.Deserialize<T>(text);
            if (value is null)
            {
                errors.Add(new ContentError(file, "The file is empty."));
            }

            return value;
        }
        catch (YamlException e)
        {
            errors.Add(new ContentError(file, $"Line {e.Start.Line}: {e.InnerException?.Message ?? e.Message}"));
            return null;
        }
    }

    private static bool CheckId(string file, string? id, List<ContentError> errors)
    {
        if (id is not null && IdPattern().IsMatch(id))
        {
            return true;
        }

        errors.Add(new ContentError(file, $"Id '{id}' must be upper case letters, digits, and underscores, starting with a letter."));
        return false;
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex IdPattern();

    private sealed class PackDto
    {
        public string? Id { get; set; }

        public int Version { get; set; }
    }

    private sealed class SkillDto
    {
        public string Id { get; set; } = "";

        public List<string> Tags { get; set; } = [];
    }
}
```


- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Sim.Tests`
Expected: all pass.

- [ ] **Step 6: Write the starter content pack (placeholders only)**

`content/pack.yaml`:

```yaml
id: CORE
version: 1
```

`content/skills/base.yaml`:

```yaml
- id: SKILL_A
- id: SKILL_B
- id: SKILL_C
- id: SKILL_D
```


- [ ] **Step 7: Commit**

```bash
git add src/Sim.Content tests/Sim.Tests content
git commit -m "Add content loader and validator"
```

---

### Task 7: SQLite legacy store and developer export

**Files:**
- Create: `src/Persistence/ILegacyStore.cs`, `src/Persistence/SqliteLegacyStore.cs`, `src/Persistence/LegacyExport.cs`
- Test: `tests/Persistence.Tests/SqliteLegacyStoreTests.cs`

**Interfaces:**
- Consumes: `LegacyState`, `CyclePhase`, and `EventEnvelope` from Tasks 3 and 5.
- Produces: `LegacyRecord(LegacyState State, string ContentPackId, int ContentPackVersion)`; `ILegacyStore { Create(LegacyRecord); Load(Guid) -> LegacyRecord?; Commit(LegacyState, IReadOnlyList<EventEnvelope>); ReadEvents(Guid) }`; `new SqliteLegacyStore(string connectionString)`; `LegacyExport.Export(ILegacyStore, Guid) -> string`; `LegacyExport.Import(ILegacyStore, string) -> Guid`.
- Postgres arrives with the online track as a second `ILegacyStore`; nothing else changes.

- [ ] **Step 1: Write the interface**

`src/Persistence/ILegacyStore.cs`:

```csharp
using Sim.Core;

namespace Persistence;

public sealed record LegacyRecord(LegacyState State, string ContentPackId, int ContentPackVersion);

public interface ILegacyStore
{
    void Create(LegacyRecord record);

    LegacyRecord? Load(Guid legacyId);

    // Saves the state and appends its new events in one transaction.
    void Commit(LegacyState state, IReadOnlyList<EventEnvelope> newEvents);

    IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId);
}
```


- [ ] **Step 2: Write the failing tests**

`tests/Persistence.Tests/SqliteLegacyStoreTests.cs`:

```csharp
using Persistence;
using Sim.Core;

namespace Persistence.Tests;

public sealed class SqliteLegacyStoreTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"starship-sim-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    private SqliteLegacyStore NewStore()
    {
        return new SqliteLegacyStore($"Data Source={path}");
    }

    private static LegacyRecord NewRecord(Guid id)
    {
        // A seed above long.MaxValue checks that the full unsigned range survives storage.
        return new LegacyRecord(new LegacyState { LegacyId = id, Seed = ulong.MaxValue - 5 }, "CORE", 1);
    }

    [Fact]
    public void A_committed_legacy_loads_back_identically()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Cycle = 4;
        record.State.Phase = CyclePhase.Aftermath;
        record.State.NextSequence = 3;
        record.State.RngStates["4:missions"] = ulong.MaxValue;
        var events = new[] { new EventEnvelope(1, 3, "A", 1, "{}"), new EventEnvelope(2, 4, "B", 2, """{"X":1}""") };

        store.Commit(record.State, events);

        LegacyRecord loaded = NewStore().Load(id)!;
        Assert.Equal(ulong.MaxValue - 5, loaded.State.Seed);
        Assert.Equal(4, loaded.State.Cycle);
        Assert.Equal(CyclePhase.Aftermath, loaded.State.Phase);
        Assert.Equal(3, loaded.State.NextSequence);
        Assert.Equal(ulong.MaxValue, loaded.State.RngStates["4:missions"]);
        Assert.Equal("CORE", loaded.ContentPackId);
        Assert.Equal(1, loaded.ContentPackVersion);
        Assert.Equal(events, NewStore().ReadEvents(id));
    }

    [Fact]
    public void A_missing_legacy_loads_as_null()
    {
        Assert.Null(NewStore().Load(Guid.NewGuid()));
    }

    // One player action is one transaction: a failed event insert must not leave the state half saved.
    [Fact]
    public void A_failed_commit_changes_nothing()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")]);
        record.State.Cycle = 9;

        Assert.ThrowsAny<Exception>(() => store.Commit(record.State, [new EventEnvelope(1, 1, "Duplicate", 1, "{}")]));

        Assert.Equal(1, store.Load(id)!.State.Cycle);
        Assert.Single(store.ReadEvents(id));
    }

    // Legacies are the isolation boundary: one legacy's events never appear in another's.
    [Fact]
    public void Events_are_isolated_by_legacy()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        store.Create(NewRecord(first));
        store.Create(NewRecord(second));
        store.Commit(NewRecord(first).State, [new EventEnvelope(1, 1, "A", 1, "{}")]);

        Assert.Empty(store.ReadEvents(second));
    }

    [Fact]
    public void An_exported_legacy_imports_into_an_empty_database()
    {
        Guid id = Guid.NewGuid();
        SqliteLegacyStore store = NewStore();
        LegacyRecord record = NewRecord(id);
        store.Create(record);
        record.State.Cycle = 2;
        store.Commit(record.State, [new EventEnvelope(1, 1, "A", 1, "{}")]);
        string json = LegacyExport.Export(store, id);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        SqliteLegacyStore empty = NewStore();
        Guid imported = LegacyExport.Import(empty, json);

        Assert.Equal(id, imported);
        Assert.Equal(2, empty.Load(id)!.State.Cycle);
        Assert.Equal(json, LegacyExport.Export(empty, id));
    }
}
```


- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/Persistence.Tests`
Expected: build error, `SqliteLegacyStore` does not exist.

- [ ] **Step 4: Implement**

`src/Persistence/SqliteLegacyStore.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sim.Core;

namespace Persistence;

public sealed class SqliteLegacyStore : ILegacyStore
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS legacy (
            legacy_id TEXT PRIMARY KEY,
            seed TEXT NOT NULL,
            cycle INTEGER NOT NULL,
            phase TEXT NOT NULL,
            next_sequence INTEGER NOT NULL,
            rng_states TEXT NOT NULL,
            content_pack_id TEXT NOT NULL,
            content_pack_version INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS event_log (
            legacy_id TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            cycle INTEGER NOT NULL,
            type TEXT NOT NULL,
            version INTEGER NOT NULL,
            payload TEXT NOT NULL,
            PRIMARY KEY (legacy_id, sequence)
        );
        """;

    private readonly string connectionString;

    public SqliteLegacyStore(string connectionString)
    {
        this.connectionString = connectionString;
        using SqliteConnection connection = Open();
        Execute(connection, null, Schema);
    }

    public void Create(LegacyRecord record)
    {
        using SqliteConnection connection = Open();
        Execute(
            connection,
            null,
            "INSERT INTO legacy VALUES ($id, $seed, $cycle, $phase, $next, $rng, $packId, $packVersion)",
            StateParameters(record.State).Append(("$packId", record.ContentPackId)).Append(("$packVersion", record.ContentPackVersion)).ToArray());
    }

    public LegacyRecord? Load(Guid legacyId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT seed, cycle, phase, next_sequence, rng_states, content_pack_id, content_pack_version FROM legacy WHERE legacy_id = $id";
        command.Parameters.AddWithValue("$id", legacyId.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var state = new LegacyState
        {
            LegacyId = legacyId,
            Seed = ulong.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            Cycle = reader.GetInt32(1),
            Phase = Enum.Parse<CyclePhase>(reader.GetString(2)),
            NextSequence = reader.GetInt64(3),
            RngStates = JsonSerializer.Deserialize<Dictionary<string, ulong>>(reader.GetString(4))!,
        };
        return new LegacyRecord(state, reader.GetString(5), reader.GetInt32(6));
    }

    public void Commit(LegacyState state, IReadOnlyList<EventEnvelope> newEvents)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        int updated = Execute(
            connection,
            transaction,
            "UPDATE legacy SET cycle = $cycle, phase = $phase, next_sequence = $next, rng_states = $rng WHERE legacy_id = $id AND seed = $seed",
            StateParameters(state).ToArray());
        if (updated != 1)
        {
            throw new InvalidOperationException($"Legacy {state.LegacyId} does not exist.");
        }

        foreach (EventEnvelope e in newEvents)
        {
            Execute(
                connection,
                transaction,
                "INSERT INTO event_log VALUES ($id, $sequence, $cycle, $type, $version, $payload)",
                ("$id", state.LegacyId.ToString()),
                ("$sequence", e.Sequence),
                ("$cycle", e.Cycle),
                ("$type", e.Type),
                ("$version", e.Version),
                ("$payload", e.Payload));
        }

        transaction.Commit();
    }

    public IReadOnlyList<EventEnvelope> ReadEvents(Guid legacyId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT sequence, cycle, type, version, payload FROM event_log WHERE legacy_id = $id ORDER BY sequence";
        command.Parameters.AddWithValue("$id", legacyId.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        var events = new List<EventEnvelope>();
        while (reader.Read())
        {
            events.Add(new EventEnvelope(reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4)));
        }

        return events;
    }

    private static IEnumerable<(string, object)> StateParameters(LegacyState state)
    {
        return
        [
            ("$id", state.LegacyId.ToString()),
            ("$seed", state.Seed.ToString(CultureInfo.InvariantCulture)),
            ("$cycle", state.Cycle),
            ("$phase", state.Phase.ToString()),
            ("$next", state.NextSequence),
            ("$rng", JsonSerializer.Serialize(state.RngStates)),
        ];
    }

    private static int Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}
```

`src/Persistence/LegacyExport.cs`:

```csharp
using System.Text.Json;
using Sim.Core;

namespace Persistence;

// Developer mode only: the way to keep a legacy safe from bugs while the game is under construction.
public static class LegacyExport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Export(ILegacyStore store, Guid legacyId)
    {
        LegacyRecord record = store.Load(legacyId) ?? throw new InvalidOperationException($"Legacy {legacyId} does not exist.");
        return JsonSerializer.Serialize(new Snapshot(record, store.ReadEvents(legacyId)), Options);
    }

    // Fails if the legacy already exists; restore into an empty database or delete the legacy first.
    public static Guid Import(ILegacyStore store, string json)
    {
        Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(json) ?? throw new InvalidOperationException("The snapshot is empty.");
        store.Create(snapshot.Legacy);
        store.Commit(snapshot.Legacy.State, snapshot.Events);
        return snapshot.Legacy.State.LegacyId;
    }

    private sealed record Snapshot(LegacyRecord Legacy, IReadOnlyList<EventEnvelope> Events);
}
```


- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/Persistence.Tests`
Expected: 5 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Persistence tests/Persistence.Tests
git commit -m "Add SQLite legacy store and developer export"
```

---

### Task 8: SimRunner, golden seed, and the check script

**Files:**
- Create: `src/SimRunner/Simulation.cs`, `check.sh`
- Modify: `src/SimRunner/Program.cs` (replace the template)
- Test: `tests/SimRunner.Tests/SimulationTests.cs`

**Interfaces:**
- Consumes: `EventBus`, `LegacyState`, `EventTypeRegistry`, `CycleModule`, `AdvancePhase`, and `ContentLoader.LoadDirectory`.
- Produces: `SimOptions(int Legacies, int Cycles, ulong Seed)`; `LegacySummary(ulong Seed, int Cycles, int Events, string LogHash)`; `SimSummary(IReadOnlyList<LegacySummary> Legacies)`; `Simulation.Run(SimOptions) -> SimSummary`; the command `SimRunner cycles [--legacies N] [--cycles N] [--seed N] [--content DIR] [--out FILE]` (exit 0 on success, 1 on content errors, and 2 on bad usage).

- [ ] **Step 1: Write the failing tests**

`tests/SimRunner.Tests/SimulationTests.cs`:

```csharp
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
```


- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/SimRunner.Tests`
Expected: build error, `Simulation` does not exist.

- [ ] **Step 3: Implement**

`src/SimRunner/Simulation.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Sim.Core;
using Sim.Cycle;

namespace SimRunner;

public sealed record SimOptions(int Legacies, int Cycles, ulong Seed);

public sealed record LegacySummary(ulong Seed, int Cycles, int Events, string LogHash);

public sealed record SimSummary(IReadOnlyList<LegacySummary> Legacies);

public static class Simulation
{
    public static SimSummary Run(SimOptions options)
    {
        var bus = new EventBus();
        CycleModule.Register(bus);
        EventTypeRegistry registry = EventTypeRegistry.FromAssemblies(typeof(CycleModule).Assembly);

        var legacies = new List<LegacySummary>();
        for (int i = 0; i < options.Legacies; i++)
        {
            ulong seed = options.Seed + (ulong)i;
            var state = new LegacyState { LegacyId = new Guid((int)(seed >> 32), 0, 0, BitConverter.GetBytes(seed)), Seed = seed };
            var log = new List<EventEnvelope>();
            while (state.Cycle <= options.Cycles)
            {
                log.AddRange(bus.Send(state, new AdvancePhase()).Select(registry.ToEnvelope));
            }

            legacies.Add(new LegacySummary(seed, options.Cycles, log.Count, Hash(log)));
        }

        return new SimSummary(legacies);
    }

    // The golden-seed fingerprint: any change to what happens, or in what order, changes it.
    private static string Hash(IEnumerable<EventEnvelope> log)
    {
        var text = new StringBuilder();
        foreach (EventEnvelope e in log)
        {
            text.Append(e.Sequence).Append('|').Append(e.Cycle).Append('|').Append(e.Type).Append('|').Append(e.Version).Append('|').Append(e.Payload).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
```

`src/SimRunner/Program.cs`:

```csharp
using System.Text.Json;
using Sim.Content;
using SimRunner;

if (args.Length == 0 || args[0] != "cycles")
{
    Console.Error.WriteLine("Usage: SimRunner cycles [--legacies N] [--cycles N] [--seed N] [--content DIR] [--out FILE]");
    return 2;
}

string Option(string name, string fallback)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

ContentLoadResult content = ContentLoader.LoadDirectory(Option("--content", "content"));
if (content.Pack is null)
{
    foreach (ContentError error in content.Errors)
    {
        Console.Error.WriteLine($"{error.File}: {error.Message}");
    }

    return 1;
}

var options = new SimOptions(int.Parse(Option("--legacies", "200")), int.Parse(Option("--cycles", "500")), ulong.Parse(Option("--seed", "1")));
SimSummary summary = Simulation.Run(options);
string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
string outFile = Option("--out", "");
if (outFile != "")
{
    File.WriteAllText(outFile, json);
}

Console.WriteLine($"Content {content.Pack.Id} v{content.Pack.Version}: {summary.Legacies.Count} legacies x {options.Cycles} cycles, {summary.Legacies.Sum(l => l.Events)} events. OK.");
return 0;
```


- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/SimRunner.Tests`
Expected: 2 tests pass. If the golden hash differs, an earlier task's code differs from this plan.

- [ ] **Step 5: Write the check script**

`check.sh`:

```bash
#!/usr/bin/env bash
# The gate every change must pass: build, tests, and a headless simulator run.
set -euo pipefail
cd "$(dirname "$0")"
dotnet build --nologo -v quiet
dotnet test --nologo --no-build -v quiet
dotnet run --no-build --project src/SimRunner -- cycles --legacies 20 --cycles 50 --seed 1 --content content
```

Then `chmod +x check.sh`.

- [ ] **Step 6: Run the gate**

Run: `./check.sh`
Expected: all tests pass, then `Content CORE v1: 20 legacies x 50 cycles, 6000 events. OK.`

- [ ] **Step 7: Commit**

```bash
git add src/SimRunner tests/SimRunner.Tests check.sh
git commit -m "Add SimRunner, golden seed, and check script"
```

---

### Task 9: Architecture rules as tests

**Files:**
- Test: `tests/Sim.Tests/ArchitectureTests.cs`

**Interfaces:**
- Consumes: the repository layout from Task 1.
- Produces: tests that fail when a `Sim.*` project references anything but `Sim.Core`, or uses a clock, a database, or ambient randomness. New `Sim.*` projects are picked up automatically.

- [ ] **Step 1: Write the tests**

`tests/Sim.Tests/ArchitectureTests.cs`:

```csharp
using System.Xml.Linq;

namespace Sim.Tests;

// GDD 7.1: the rules that let a new mechanic be added without rewriting old modules.
public class ArchitectureTests
{
    private static readonly string Root = FindRoot();

    private static readonly string[] Banned =
    [
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
        "Stopwatch",
        "new Random(",
        "Random.Shared",
        "Guid.NewGuid",
        "Microsoft.Data",
        "Npgsql",
    ];

    public static TheoryData<string> SimProjects()
    {
        var data = new TheoryData<string>();
        foreach (string directory in Directory.GetDirectories(Path.Combine(Root, "src"), "Sim.*"))
        {
            data.Add(Path.GetFileName(directory));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SimProjects))]
    public void Sim_projects_reference_only_Sim_Core(string project)
    {
        XDocument csproj = XDocument.Load(Path.Combine(Root, "src", project, project + ".csproj"));
        IEnumerable<string> references = csproj.Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', '/')));

        Assert.All(references, reference => Assert.Equal("Sim.Core", reference));
    }

    [Theory]
    [MemberData(nameof(SimProjects))]
    public void Sim_projects_have_no_clock_database_or_ambient_randomness(string project)
    {
        IEnumerable<string> sources = Directory
            .EnumerateFiles(Path.Combine(Root, "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"));

        foreach (string file in sources)
        {
            string text = File.ReadAllText(file);
            Assert.All(Banned, banned => Assert.False(text.Contains(banned), $"{Path.GetRelativePath(Root, file)} uses {banned}."));
        }
    }

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
```


- [ ] **Step 2: Run the tests**

Run: `dotnet test tests/Sim.Tests`
Expected: all pass (27 cases in total for this project).

- [ ] **Step 3: Prove the tests can fail**

Temporarily add `private static readonly Guid Bad = Guid.NewGuid();` to `CycleModule`. Run `dotnet test tests/Sim.Tests`. Expected: FAIL with `src/Sim.Cycle/CycleModule.cs uses Guid.NewGuid.` Remove the line and run again. Expected: PASS.

- [ ] **Step 4: Run the gate and commit**

Run: `./check.sh`
Expected: green.

```bash
git add tests/Sim.Tests
git commit -m "Add architecture rules as tests"
```

---

## Done when

- `./check.sh` is green from a clean clone.
- The roadmap's Phase 0 result holds: nothing to play, and a green simulator run.

## Deliberately not in Phase 0

- `Web`, `Sim.Missions`, `Sim.Storylets`, `Sim.Crew`, `Sim.Chronicle`, and `Sim.Narrative`: each arrives with the Phase 1 slice that first needs it (`docs/plans/phase-1-outline.md`).
- The expression language and quality view: first needed by storylets (slice 1e).
- Postgres, accounts, and hosting: the online track.
- Simulator policies, metrics, and alarms: there is nothing to measure until missions exist (slices 1a and 1b).

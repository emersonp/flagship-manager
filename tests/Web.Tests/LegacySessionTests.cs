using Persistence;
using Sim.Core;

namespace Web.Tests;

public sealed class LegacySessionTests : IDisposable
{
    private readonly TestApp app = new();

    public void Dispose()
    {
        app.Dispose();
    }

    [Fact]
    public async Task A_second_run_waits_for_the_first_and_both_runs_events_are_stored_in_order()
    {
        Guid id = await app.CreateAtOrders("LEGACY", 1);
        var firstEntered = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        int secondEntered = 0;

        Task first = Task.Run(() => app.Sessions.Run<object>(app.Live, id, legacy =>
        {
            firstEntered.SetResult();
            gate.Task.Wait();
            legacy.Send(new AdvancePhase());
            return new object();
        }));
        await firstEntered.Task;
        var secondWaiting = new TaskCompletionSource();
        app.Sessions.BeforeLock = _ => secondWaiting.TrySetResult();
        Task second = Task.Run(() => app.Sessions.Run<object>(app.Live, id, legacy =>
        {
            Interlocked.Increment(ref secondEntered);
            legacy.Send(new AdvancePhase());
            return new object();
        }));
        await secondWaiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        // The one wait kept: it shows the second run does not get in, which no signal can say.
        await Task.Delay(100);
        int enteredWhileHeld = Volatile.Read(ref secondEntered);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(0, enteredWhileHeld);
        IReadOnlyList<EventEnvelope> events = app.Game.Store.ReadEvents(id);
        Assert.Equal(Enumerable.Range(1, events.Count).Select(n => (long)n), events.Select(e => e.Sequence));
        Assert.Equal(CyclePhase.Resolution, (await app.Sessions.Read(app.Live, id))!.State.Phase);
        Assert.Equal(events.Count + 1, (await app.Sessions.Read(app.Live, id))!.State.NextSequence);
    }

    [Fact]
    public async Task A_rejected_command_leaves_the_stored_legacy_unchanged()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY");
        LoadedLegacy before = (await app.Sessions.Read(app.Live, id))!;
        int eventsBefore = app.Game.Store.ReadEvents(id).Count;

        await Assert.ThrowsAsync<CommandRejectedException>(() => app.Sessions.Run<object>(app.Live, id, legacy =>
        {
            legacy.Send(new AdvancePhase());
            legacy.Send(new FoundLegacy());
            return new object();
        }));

        LoadedLegacy after = (await app.Sessions.Read(app.Live, id))!;
        Assert.Equal(before.State.NextSequence, after.State.NextSequence);
        Assert.Equal(before.State.Phase, after.State.Phase);
        Assert.Equal(eventsBefore, app.Game.Store.ReadEvents(id).Count);
    }

    [Fact]
    public async Task Run_on_an_unknown_legacy_returns_null()
    {
        Assert.Null(await app.Sessions.Run<object>(app.Live, Guid.NewGuid(), _ => new object()));
    }

    [Fact]
    public async Task A_created_legacy_has_its_founding_events_and_loads_as_founded()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY");

        IReadOnlyList<EventEnvelope> events = app.Game.Store.ReadEvents(id);
        LoadedLegacy loaded = (await app.Sessions.Read(app.Live, id))!;
        Assert.NotEmpty(events);
        Assert.Equal(events.Count + 1, loaded.State.NextSequence);
        Assert.Equal(CyclePhase.Itinerary, loaded.State.Phase);
        await Assert.ThrowsAsync<CommandRejectedException>(() => app.Sessions.Run<object>(app.Live, id, legacy =>
        {
            legacy.Send(new FoundLegacy());
            return new object();
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_blank_or_null_name_is_refused_and_nothing_is_created(string? name)
    {
        await Assert.ThrowsAsync<InvalidLegacyNameException>(() => app.Sessions.CreateLegacy(app.Live, name!));

        Assert.Empty(app.Game.Store.ListLegacies());
    }

    // A lock is kept only for a legacy that exists, so asking after unknown ids does not grow the table.
    [Fact]
    public async Task No_lock_is_kept_for_an_id_that_does_not_exist()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY");
        int locks = app.Sessions.LockCount;

        Assert.Null(await app.Sessions.Read(app.Live, Guid.NewGuid()));
        Assert.Null(await app.Sessions.Run<object>(app.Live, Guid.NewGuid(), _ => new object()));
        await Assert.ThrowsAsync<InvalidLegacyNameException>(() => app.Sessions.CreateLegacy(app.Live, " "));
        Assert.NotNull(await app.Sessions.Read(app.Live, id));

        Assert.Equal(locks, app.Sessions.LockCount);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_wait_for_the_lock()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.Sessions.Read(app.Live, id, cancelled.Token));
    }
}

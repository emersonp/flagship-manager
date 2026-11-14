using System.Collections.Concurrent;
using System.Security.Cryptography;
using Persistence;
using Sim.Core;

namespace Web;

// Lock, load, send, commit: one legacy at a time, on a state no other request holds.
public sealed class LegacySession(GameServices services)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();

    // A test hook: runs after a run's commit, inside the lock.
    internal Action<Guid>? AfterCommit { get; set; }

    internal int LockCount => locks.Count;

    // Runs work under the legacy's lock on a freshly loaded state, commits what it published, and returns its result.
    // Returns null when the legacy does not exist. A rejected command rolls nothing forward and is rethrown.
    public async Task<T?> Run<T>(Guid legacyId, Func<LoadedLegacy, T> work, CancellationToken cancellation = default)
        where T : class
    {
        using Held held = await Hold(legacyId, cancellation);
        LoadedLegacy? legacy = Load(held);
        if (legacy is null)
        {
            return null;
        }

        long loadedAt = legacy.State.NextSequence;
        T result = work(legacy);
        services.Store.Commit(legacy.State, legacy.Envelopes, loadedAt);
        AfterCommit?.Invoke(legacyId);
        return result;
    }

    public async Task<LoadedLegacy?> Read(Guid legacyId, CancellationToken cancellation = default)
    {
        using Held held = await Hold(legacyId, cancellation);
        return Load(held);
    }

    public Task<Guid> CreateLegacy(string name, CancellationToken cancellation = default)
    {
        return CreateLegacy(name, RandomSeed(), cancellation);
    }

    // The seed is a test hook: the public overload always draws one.
    internal async Task<Guid> CreateLegacy(string name, ulong seed, CancellationToken cancellation = default)
    {
        Guid id = Guid.NewGuid();
        using Held held = await Hold(id, cancellation);
        var state = new LegacyState { LegacyId = id, Seed = seed };
        var record = new LegacyRecord(state, services.Pack.Id, services.Pack.Version, name, services.Clock.GetUtcNow());
        var legacy = new LoadedLegacy(record, services);
        // Founded in memory, then stored with its events in one transaction: a failure leaves nothing behind, not even the lock.
        legacy.Send(new FoundLegacy());
        try
        {
            services.Store.Create(record, legacy.Envelopes);
        }
        catch
        {
            held.Forget();
            throw;
        }

        return id;
    }

    // Loads under the held lock; a legacy that does not exist leaves no lock behind.
    private LoadedLegacy? Load(Held held)
    {
        LegacyRecord? record = services.Store.Load(held.LegacyId, services.Game.ModuleTypes);
        if (record is null)
        {
            held.Forget();
            return null;
        }

        return new LoadedLegacy(record, services);
    }

    private static ulong RandomSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private async Task<Held> Hold(Guid legacyId, CancellationToken cancellation)
    {
        SemaphoreSlim gate = locks.GetOrAdd(legacyId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellation);
        return new Held(this, legacyId, gate);
    }

    private sealed class Held(LegacySession owner, Guid legacyId, SemaphoreSlim gate) : IDisposable
    {
        public Guid LegacyId { get; } = legacyId;

        // Drops the table's entry for an id with nothing stored; only this exact lock is removed.
        public void Forget()
        {
            owner.locks.TryRemove(new KeyValuePair<Guid, SemaphoreSlim>(LegacyId, gate));
        }

        public void Dispose()
        {
            gate.Release();
        }
    }
}

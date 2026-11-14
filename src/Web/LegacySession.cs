using System.Collections.Concurrent;
using System.Security.Cryptography;
using Persistence;
using Sim.Core;

namespace Web;

// Lock, load, send, commit: one legacy at a time, on a state no other request holds.
public sealed class LegacySession(GameServices services)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> locks = new();

    // Runs work under the legacy's lock on a freshly loaded state, commits what it published, and returns its result.
    // Returns null when the legacy does not exist. A rejected command rolls nothing forward and is rethrown.
    public async Task<T?> Run<T>(Guid legacyId, Func<LoadedLegacy, T> work, CancellationToken cancellation = default)
        where T : class
    {
        using IDisposable held = await Hold(legacyId, cancellation);
        LoadedLegacy? legacy = Load(legacyId);
        if (legacy is null)
        {
            return null;
        }

        long loadedAt = legacy.State.NextSequence;
        T result = work(legacy);
        services.Store.Commit(legacy.State, legacy.Envelopes, loadedAt);
        return result;
    }

    public async Task<LoadedLegacy?> Read(Guid legacyId, CancellationToken cancellation = default)
    {
        using IDisposable held = await Hold(legacyId, cancellation);
        return Load(legacyId);
    }

    public async Task<Guid> CreateLegacy(string name, CancellationToken cancellation = default)
    {
        Guid id = Guid.NewGuid();
        using IDisposable held = await Hold(id, cancellation);
        var state = new LegacyState { LegacyId = id, Seed = RandomSeed() };
        var record = new LegacyRecord(state, services.Pack.Id, services.Pack.Version, name, services.Clock.GetUtcNow());
        var legacy = new LoadedLegacy(record, services);
        // Founded in memory, then stored with its events in one transaction: a failure leaves nothing behind.
        legacy.Send(new FoundLegacy());
        services.Store.Create(record, legacy.Envelopes);
        return id;
    }

    private LoadedLegacy? Load(Guid legacyId)
    {
        LegacyRecord? record = services.Store.Load(legacyId, services.Game.ModuleTypes);
        return record is null ? null : new LoadedLegacy(record, services);
    }

    private static ulong RandomSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private async Task<IDisposable> Hold(Guid legacyId, CancellationToken cancellation)
    {
        SemaphoreSlim gate = locks.GetOrAdd(legacyId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellation);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose()
        {
            gate.Release();
        }
    }
}

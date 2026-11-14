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
        EventTypeRegistry registry = EventTypeRegistry.FromAssemblies(typeof(LegacyState).Assembly);

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

using System.Security.Cryptography;
using System.Text;
using GameHost;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;

namespace SimRunner;

public sealed record SimOptions(int Legacies, int Cycles, ulong Seed);

public sealed record LegacySummary(
    ulong Seed,
    int Cycles,
    int Events,
    string LogHash,
    IReadOnlyDictionary<MissionOutcome, int> Missions,
    int Deaths,
    int Alive,
    int HighestLevel);

public sealed record SimSummary(IReadOnlyList<LegacySummary> Legacies);

public static class Simulation
{
    public static SimSummary Run(SimOptions options, ContentPack pack)
    {
        Game game = Game.Build(pack);
        var legacies = new List<LegacySummary>();
        for (int i = 0; i < options.Legacies; i++)
        {
            ulong seed = options.Seed + (ulong)i;
            var state = new LegacyState { LegacyId = new Guid((int)(seed >> 32), 0, 0, BitConverter.GetBytes(seed)), Seed = seed };
            var events = new List<PublishedEvent>();
            while (state.Cycle <= options.Cycles)
            {
                events.AddRange(AutoCaptain.PlayCycle(game, state, pack));
            }

            legacies.Add(Summarise(game, state, seed, options.Cycles, events));
        }

        return new SimSummary(legacies);
    }

    private static LegacySummary Summarise(Game game, LegacyState state, ulong seed, int cycles, List<PublishedEvent> events)
    {
        Dictionary<MissionOutcome, int> missions = Enum.GetValues<MissionOutcome>().ToDictionary(o => o, _ => 0);
        foreach (MissionEnded ended in events.Select(e => e.Event).OfType<MissionEnded>())
        {
            missions[ended.Result.Outcome]++;
        }

        IEnumerable<CrewMember> members = state.TryModule(CrewModule.Name, out CrewState? crew) ? crew.Members : [];
        int deaths = events.Count(e => e.Event is CrewDied);
        int alive = game.Bus.View<IRosterView>(state).Roster.Count;
        int highest = members.SelectMany(m => m.Skills.Values).Select(s => s.Level).DefaultIfEmpty(0).Max();
        return new LegacySummary(seed, cycles, events.Count, Hash(events.Select(game.Events.ToEnvelope)), missions, deaths, alive, highest);
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

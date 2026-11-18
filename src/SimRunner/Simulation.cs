using System.Security.Cryptography;
using System.Text;
using GameHost;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;

namespace SimRunner;

// Answers: how the captain answers storylets and vignettes (1e part three).
public sealed record SimOptions(int Legacies, int Cycles, ulong Seed, AnswerPolicy Answers = AnswerPolicy.First);

public sealed record LegacySummary(
    ulong Seed,
    int Cycles,
    int Events,
    string LogHash,
    IReadOnlyDictionary<MissionOutcome, int> Missions,
    int Deaths,
    int Alive,
    int HighestLevel,
    int StoryletsOffered,
    int StoryletsAnswered,
    int VignettesArrived,
    int VignettesDefaulted,
    int MoraleLow,
    int MoraleSteady,
    int MoraleHigh)
{
    // What fired (1e part three, design 4.10), by key: a storylet or vignette's id when it arrived, and "{id} {option}" when that
    // option was taken (a vignette's default included).
    public IReadOnlyDictionary<string, int> Fired { get; init; } = new Dictionary<string, int>();

    // How many times each trait went from 0 to more, anywhere.
    public IReadOnlyDictionary<string, int> TraitsSet { get; init; } = new Dictionary<string, int>();
}

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
                events.AddRange(AutoCaptain.PlayCycle(game, state, pack, options.Answers));
            }

            legacies.Add(Summarise(game, state, pack, seed, options.Cycles, events));
        }

        return new SimSummary(legacies);
    }

    private static LegacySummary Summarise(Game game, LegacyState state, ContentPack pack, ulong seed, int cycles, List<PublishedEvent> events)
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
        int offered = events.Count(e => e.Event is StoryletOffered);
        int answered = events.Count(e => e.Event is StoryletAnswered);
        int arrived = events.Count(e => e.Event is VignetteArrived);
        int defaulted = events.Count(e => e.Event is VignetteAnswered { Defaulted: true });
        // The living crew's morale at the end, in the three bands that change a check (GDD 10's morale distribution).
        List<int> morale = members.Where(m => m.Status != CrewStatus.Dead).Select(m => pack.CrewTuning.MoraleBonus(m.Morale)).ToList();
        var fired = new Dictionary<string, int>();
        void Count(string key)
        {
            fired[key] = fired.GetValueOrDefault(key) + 1;
        }

        var set = new Dictionary<string, int>();
        foreach (IEvent e in events.Select(p => p.Event))
        {
            switch (e)
            {
                case StoryletOffered o:
                    Count(o.StoryletId);
                    break;
                case StoryletAnswered a:
                    Count($"{a.StoryletId} {a.OptionId}");
                    break;
                case VignetteArrived v:
                    Count(v.VignetteId);
                    break;
                case VignetteAnswered { OptionId: { } option } v:
                    Count($"{v.VignetteId} {option}");
                    break;
                case TraitChanged { From: 0, To: > 0 } t:
                    set[t.Trait] = set.GetValueOrDefault(t.Trait) + 1;
                    break;
            }
        }

        return new LegacySummary(
            seed, cycles, events.Count, Hash(events.Select(game.Events.ToEnvelope)), missions, deaths, alive, highest, offered, answered,
            arrived, defaulted, morale.Count(b => b < 0), morale.Count(b => b == 0), morale.Count(b => b > 0))
        {
            Fired = fired,
            TraitsSet = set,
        };
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

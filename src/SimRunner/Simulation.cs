using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameHost;
using Sim.Core;
using Sim.Core.Content;
using Sim.Crew;
using Calendar = Sim.Core.Calendar;

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

    // From the contest each itinerary opened with (1e part four, Task 8), by storylet or vignette id: the cycles it was eligible
    // (design 4.10), cast (arrived or lost on the budget), lost on the budget, and out of tries.
    public IReadOnlyDictionary<string, int> Eligible { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> Cast { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> Lost { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> OutOfTries { get; init; } = new Dictionary<string, int>();

    // Each option settled by a check, by "{id} {option}": the times it was taken, and of those the times the roll succeeded.
    public IReadOnlyDictionary<string, int> ChecksTaken { get; init; } = new Dictionary<string, int>();

    public IReadOnlyDictionary<string, int> ChecksSucceeded { get; init; } = new Dictionary<string, int>();

    // The calendar (slice 1e-calendar, Task 6): how many turns rolled each length, by days, and the day and date the legacy ended on.
    public IReadOnlyDictionary<int, int> CycleLengths { get; init; } = new Dictionary<int, int>();

    public int LastDay { get; init; }

    public string LastDate { get; init; } = "";
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
            var contests = new List<Contest>();
            while (state.Cycle <= options.Cycles)
            {
                // The commands PlayCycle would send, one at a time, so the contest each itinerary opened with is read as it opens.
                IReadOnlyList<PublishedEvent> step = AutoCaptain.Step(game, state, pack, options.Answers);
                events.AddRange(step);
                if (step.Any(e => e.Event is PhaseEntered { Phase: CyclePhase.Itinerary }) && game.Bus.View<IStoryletView>(state).Opened is { } opened)
                {
                    contests.Add(opened);
                }
            }

            legacies.Add(Summarise(game, state, pack, seed, options.Cycles, events, contests));
        }

        return new SimSummary(legacies);
    }

    private static LegacySummary Summarise(Game game, LegacyState state, ContentPack pack, ulong seed, int cycles, List<PublishedEvent> events, List<Contest> contests)
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
        var taken = new Dictionary<string, int>();
        var succeeded = new Dictionary<string, int>();
        var lengths = new Dictionary<int, int>();
        foreach (IEvent e in events.Select(p => p.Event))
        {
            switch (e)
            {
                case StoryletOffered o:
                    Count(o.StoryletId);
                    break;
                case StoryletAnswered a:
                    Count($"{a.StoryletId} {a.OptionId}");
                    if (a.Roll is { } roll)
                    {
                        taken[$"{a.StoryletId} {a.OptionId}"] = taken.GetValueOrDefault($"{a.StoryletId} {a.OptionId}") + 1;
                        succeeded[$"{a.StoryletId} {a.OptionId}"] = succeeded.GetValueOrDefault($"{a.StoryletId} {a.OptionId}") + (roll.Success ? 1 : 0);
                    }

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
                case CycleLengthRolled r:
                    lengths[r.Days] = lengths.GetValueOrDefault(r.Days) + 1;
                    break;
            }
        }

        // What each itinerary's contest said of every storylet and vignette, whether or not it arrived.
        List<Contender> contenders = contests.SelectMany(c => c.Storylets.Concat(c.Vignettes)).ToList();
        Dictionary<string, int> Cycles(Func<Contender, bool> which)
        {
            return contenders.Where(which).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Count());
        }

        return new LegacySummary(
            seed, cycles, events.Count, Hash(events.Select(game.Events.ToEnvelope)), missions, deaths, alive, highest, offered, answered,
            arrived, defaulted, morale.Count(b => b < 0), morale.Count(b => b == 0), morale.Count(b => b > 0))
        {
            Fired = fired,
            TraitsSet = set,
            Eligible = Cycles(c => c.Eligible),
            Cast = Cycles(c => c.Fate is ContestFate.Arrived or ContestFate.LostOnBudget),
            Lost = Cycles(c => c.Fate == ContestFate.LostOnBudget),
            OutOfTries = Cycles(c => c.Fate == ContestFate.OutOfTries),
            ChecksTaken = taken,
            ChecksSucceeded = succeeded,
            CycleLengths = lengths,
            LastDay = state.Day,
            LastDate = ShortDate(pack.Calendar, state.Day),
        };
    }

    // As the web's short date ("13 Vortun 105"; Web.Bridge.Dates.Short), which the simulator does not reference.
    public static string ShortDate(CalendarDef calendar, int day)
    {
        CalendarDate date = Calendar.Date(calendar, day);
        return string.Create(CultureInfo.InvariantCulture, $"{date.Day} {calendar.Months[date.Month].Name} {date.Year}");
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

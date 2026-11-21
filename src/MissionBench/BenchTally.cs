using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;

namespace MissionBench;

// The bench's figures, fed one run at a time (slice 1f part one, design 4.7). Figures() can be read at any point and is the figures
// of the runs added so far, so a run can be cancelled, report progress, or be interleaved with another and still be read.
public sealed class BenchTally(ContentPack pack)
{
    private readonly Dictionary<MissionOutcome, int> outcomes = Enum.GetValues<MissionOutcome>().ToDictionary(o => o, _ => 0);
    private readonly SortedDictionary<string, double> experience = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, double> experienceSquares = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, EncounterTally> encounters = new(StringComparer.Ordinal);
    private readonly List<StageTally> stages = [];
    private readonly Dictionary<string, List<string>> positions = pack.Encounters.ToDictionary(e => e.Id, e => e.Approaches.Select(a => a.Id).ToList());
    private int crewMissions;
    private int deaths;
    private int incapacitated;
    private int calls;
    private int initiatives;
    private long callsSquares;
    private long initiativesSquares;
    private int worst = -1;
    private ulong worstSeed;

    public int Runs { get; private set; }

    public void Add(MissionTrace trace)
    {
        Runs++;
        outcomes[trace.Result.Outcome]++;
        calls += trace.Result.Calls;
        initiatives += trace.Result.Initiatives;
        callsSquares += (long)trace.Result.Calls * trace.Result.Calls;
        initiativesSquares += (long)trace.Result.Initiatives * trace.Result.Initiatives;
        crewMissions += trace.Result.Crew.Count;
        int harm = 0;
        foreach (CrewResult crew in trace.Result.Crew)
        {
            double gained = crew.Experience.Values.Sum();
            experience[crew.Id] = experience.GetValueOrDefault(crew.Id) + gained;
            experienceSquares[crew.Id] = experienceSquares.GetValueOrDefault(crew.Id) + (gained * gained);
            deaths += crew.Status == MemberStatus.Dead ? 1 : 0;
            incapacitated += crew.Status == MemberStatus.Incapacitated ? 1 : 0;
            harm += crew.Status == MemberStatus.Dead ? 10 : crew.Status == MemberStatus.Incapacitated ? 1 : 0;
        }

        // The worst run is the one with the most harm, the lowest seed among equals: the first run with the most harm when fed in
        // seed order, as before the tally, and the same seed whatever order the runs are fed in.
        if (harm > worst || (harm == worst && trace.Seed < worstSeed))
        {
            worst = harm;
            worstSeed = trace.Seed;
        }

        foreach (Beat beat in trace.Beats)
        {
            switch (beat)
            {
                case StageBeat stage:
                    while (stages.Count <= stage.Cursor)
                    {
                        stages.Add(new StageTally());
                    }

                    StageTally at = stages[stage.Cursor];
                    at.Reached++;
                    at.Momentum += stage.Momentum;
                    at.Strain += stage.Strain;
                    at.MomentumSquares += (double)stage.Momentum * stage.Momentum;
                    at.StrainSquares += (double)stage.Strain * stage.Strain;
                    break;
                case CheckBeat check:
                    EncounterTally tally = Tally(check.EncounterId);
                    tally.Checks++;
                    int position = positions[check.EncounterId].IndexOf(check.Approach);
                    tally.ByPosition[position]++;
                    tally.Narrowed += check.Offered < positions[check.EncounterId].Count ? 1 : 0;
                    tally.Tiers[check.Tier]++;
                    break;
                case InjuryBeat { Outcome: InjuryOutcome.Dead } injury:
                    Tally(injury.EncounterId).Deaths++;
                    break;
                case InjuryBeat { Outcome: InjuryOutcome.Incapacitated } injury:
                    Tally(injury.EncounterId).Incapacitated++;
                    break;
                default:
                    break;
            }
        }
    }

    // With no runs added, the means are empty and the worst seed is 0.
    public BenchFigures Figures()
    {
        return new BenchFigures(
            Runs,
            crewMissions,
            new Dictionary<MissionOutcome, int>(outcomes),
            deaths,
            incapacitated,
            calls,
            callsSquares,
            initiatives,
            initiativesSquares,
            experience.ToDictionary(e => e.Key, e => e.Value / Runs),
            experienceSquares.ToDictionary(e => e.Key, e => e.Value),
            encounters.Select(e => new EncounterFigures(e.Key, e.Value.Checks, [.. e.Value.ByPosition], e.Value.Narrowed, new Dictionary<Tier, int>(e.Value.Tiers), e.Value.Deaths, e.Value.Incapacitated)).ToList(),
            stages.Select((s, i) => new StageFigures(i + 1, s.Reached, s.Momentum / s.Reached, s.Strain / s.Reached, s.MomentumSquares, s.StrainSquares)).ToList(),
            worstSeed);
    }

    private EncounterTally Tally(string id)
    {
        if (!encounters.TryGetValue(id, out EncounterTally? tally))
        {
            tally = new EncounterTally();
            encounters[id] = tally;
        }

        return tally;
    }

    private sealed class EncounterTally
    {
        public int Checks { get; set; }

        public int[] ByPosition { get; } = new int[EncounterDef.MaxApproaches];

        public int Narrowed { get; set; }

        public int Deaths { get; set; }

        public int Incapacitated { get; set; }

        public Dictionary<Tier, int> Tiers { get; } = Enum.GetValues<Tier>().ToDictionary(t => t, _ => 0);
    }

    private sealed class StageTally
    {
        public int Reached { get; set; }

        public double Momentum { get; set; }

        public double Strain { get; set; }

        public double MomentumSquares { get; set; }

        public double StrainSquares { get; set; }
    }
}

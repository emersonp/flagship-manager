using Sim.Missions;

namespace MissionBench;

public sealed record EncounterFigures(string EncounterId, int Checks, int Careful, int Fast, IReadOnlyDictionary<Tier, int> Tiers, int Deaths, int Incapacitated);

public sealed record StageFigures(int Stage, int Reached, double Momentum, double Strain);

// Counts and means from one bench (GDD 10.1). Rates are derived when printed.
public sealed record BenchFigures(
    int Runs,
    int CrewMissions,
    IReadOnlyDictionary<MissionOutcome, int> Outcomes,
    int Deaths,
    int Incapacitated,
    int Calls,
    int Notices,
    IReadOnlyDictionary<string, double> ExperienceByMember,
    IReadOnlyList<EncounterFigures> Encounters,
    IReadOnlyList<StageFigures> Stages,
    ulong WorstSeed);

public sealed record MissionTrace(ulong Seed, Team Team, IReadOnlyList<Beat> Beats, MissionResult Result);

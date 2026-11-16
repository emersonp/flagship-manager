using Sim.Core;
using Sim.Missions;

namespace MissionBench;

// ByPosition counts the checks taken by each approach in the order the encounter lists them; it is always four long.
// Narrowed counts the checks where a requirement left fewer approaches on offer than the encounter has.
public sealed record EncounterFigures(string EncounterId, int Checks, IReadOnlyList<int> ByPosition, int Narrowed, IReadOnlyDictionary<Tier, int> Tiers, int Deaths, int Incapacitated);

public sealed record StageFigures(int Position, int Reached, double Momentum, double Strain);

// Counts and means from one bench (GDD 10.1). Rates are derived when printed.
public sealed record BenchFigures(
    int Runs,
    int CrewMissions,
    IReadOnlyDictionary<MissionOutcome, int> Outcomes,
    int Deaths,
    int Incapacitated,
    int Calls,
    int Initiatives,
    IReadOnlyDictionary<string, double> ExperienceByMember,
    IReadOnlyList<EncounterFigures> Encounters,
    IReadOnlyList<StageFigures> Stages,
    ulong WorstSeed);

public sealed record MissionTrace(ulong Seed, Team Team, IReadOnlyList<Beat> Beats, MissionResult Result);

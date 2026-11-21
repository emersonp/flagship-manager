using Sim.Core;
using Sim.Missions;

namespace MissionBench;

// ByPosition counts the checks taken by each approach in the order the encounter lists them; it is always four long.
// Narrowed counts the checks where a requirement left fewer approaches on offer than the encounter has. ChecksSquares is the sum over
// the runs of the square of each run's checks here (0 for a run that never reached it), so the checks per run can be judged as a mean
// (slice 1f part one, design 4.8; Task 10); the command line ignores it.
public sealed record EncounterFigures(string EncounterId, int Checks, IReadOnlyList<int> ByPosition, int Narrowed, IReadOnlyDictionary<Tier, int> Tiers, int Deaths, int Incapacitated, long ChecksSquares);

// Momentum and Strain are means over the runs that reached the stage; MomentumSquares and StrainSquares are the sums of their squares
// over the same runs, for the noise test (slice 1f part one, design 4.8).
public sealed record StageFigures(int Position, int Reached, double Momentum, double Strain, double MomentumSquares, double StrainSquares);

// Counts and means from one bench (GDD 10.1). Rates are derived when printed. The *Squares are sums of squares, one term a run (calls
// and initiatives in a run, a member's experience in a run), so a mean's spread can be judged (design 4.8); the command line ignores them.
public sealed record BenchFigures(
    int Runs,
    int CrewMissions,
    IReadOnlyDictionary<MissionOutcome, int> Outcomes,
    int Deaths,
    int Incapacitated,
    int Calls,
    long CallsSquares,
    int Initiatives,
    long InitiativesSquares,
    IReadOnlyDictionary<string, double> ExperienceByMember,
    IReadOnlyDictionary<string, double> ExperienceSquaresByMember,
    IReadOnlyList<EncounterFigures> Encounters,
    IReadOnlyList<StageFigures> Stages,
    ulong WorstSeed);

// State: the mission's last state, for replay's report (slice 1f part one, Task 12).
public sealed record MissionTrace(ulong Seed, Team Team, IReadOnlyList<Beat> Beats, MissionResult Result, MissionState State);

// One cell of Every objective (design 4.8): an objective under one strength, or under the real team (Strength null). Refused, with no
// figures, when the real team's size is outside what the objective takes.
public sealed record ObjectiveCell(string ObjectiveId, TeamStrength? Strength, BenchFigures? Figures, string? Refused);

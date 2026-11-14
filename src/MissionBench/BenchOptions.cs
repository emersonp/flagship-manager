using Sim.Missions;

namespace MissionBench;

public enum TeamStrength
{
    Rookie,
    Mixed,
    Veteran,
}

public enum LeadStyle
{
    Cautious,
    Average,
    Reckless,
}

// How the bench answers calls. BackLead backs the lead's preference, but takes the withdrawal when the abort call comes.
public enum CallPolicy
{
    BackLead,
    Bold,
    Safe,
    Withdraw,
}

public sealed record BenchOptions(string ObjectiveId)
{
    public int Runs { get; init; } = 10000;

    public ulong Seed { get; init; } = 1;

    public TeamStrength Strength { get; init; } = TeamStrength.Mixed;

    public LeadStyle Lead { get; init; } = LeadStyle.Average;

    public Parameters Parameters { get; init; } = new();

    public IReadOnlyList<string> GearTags { get; init; } = [];

    public CallPolicy Policy { get; init; } = CallPolicy.BackLead;

    public bool FixedDraw { get; init; }

    // A real team, used for every run in place of generated ones.
    public Team? Team { get; init; }
}

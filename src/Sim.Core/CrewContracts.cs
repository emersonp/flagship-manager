namespace Sim.Core;

// Contracts of the crew module's morale (1e part two, design 4.1), here so other modules can change it and read its changes.
// Handled by the crew module, which clamps the result to its tuning.
// Cause is one of MoraleCauses: an answer's, or a trait rule's (1e part three).
public sealed record AdjustMorale(string CrewId, int Delta, string Cause = MoraleCauses.Answer) : ICommand;

// Cause is one of MoraleCauses.
[EventType("MoraleChanged", 1)]
public sealed record MoraleChanged(string CrewId, int From, int To, string Cause) : IEvent;

public static class MoraleCauses
{
    public const string Mission = "mission";
    public const string Hurt = "hurt";
    public const string Death = "death";
    public const string Rest = "rest";
    public const string Drift = "drift";
    public const string Answer = "answer";

    // A trait's rule: at launch, or when a pair's target dies (1e part three).
    public const string Trait = "trait";
}

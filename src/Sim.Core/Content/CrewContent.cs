namespace Sim.Core.Content;

// Pronouns names a pronoun set in the pack; null means "they".
public sealed record CrewDef(string Id, int Seniority, IReadOnlyDictionary<string, int> Skills, int Caution, int Discipline, string? Name = null, string? Pronouns = null);

public sealed record GearDef(string Id, string Tag, string? Name = null);

// Name is display only; the id stays the key.
// Offer maps a number of objectives to the weight of a site offering that many.
public sealed record AssignmentDef(string Id, IReadOnlyList<string> ObjectiveTags, IReadOnlyDictionary<int, int> Offer, int LoadoutLimit, string? Name = null);

// Every number the crew module uses (design section 6).
public sealed record CrewTuning
{
    public required int XpBase { get; init; }

    public required int MaxLevel { get; init; }

    public required int LightInjuryWeeks { get; init; }

    public required int IncapacitatedWeeks { get; init; }

    public required int StartingLoyalty { get; init; }

    public required int LoyaltyPerPoint { get; init; }

    public required int LoyaltyMin { get; init; }

    public required int LoyaltyMax { get; init; }

    // Morale (1e part two, design 4.1). A member at or below MoraleLow takes -MoraleCheckBonus on every check, and one at or
    // above MoraleHigh takes +MoraleCheckBonus.
    public required int MoraleResting { get; init; }

    public required int MoraleMin { get; init; }

    public required int MoraleMax { get; init; }

    public required int MoraleLow { get; init; }

    public required int MoraleHigh { get; init; }

    public required int MoraleCheckBonus { get; init; }

    // Each week, towards resting and never past it.
    public required int MoraleDrift { get; init; }

    // A cycle not sent, while fit: up to resting and never past it.
    public required int MoraleRested { get; init; }

    // For each light injury on a mission.
    public required int MoraleHurt { get; init; }

    public required int MoraleIncapacitated { get; init; }

    // To every living crew member, for each death.
    public required int MoraleCrewmateDied { get; init; }

    // To each member of the team who came home.
    public required IReadOnlyDictionary<MissionOutcome, int> MoraleResult { get; init; }

    // The morale bonus on a check: minus at low, plus at high.
    public int MoraleBonus(int morale)
    {
        return morale <= MoraleLow ? -MoraleCheckBonus : morale >= MoraleHigh ? MoraleCheckBonus : 0;
    }
}

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
}

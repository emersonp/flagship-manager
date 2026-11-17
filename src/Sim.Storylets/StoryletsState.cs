using System.Text.Json.Serialization;

namespace Sim.Storylets;

// One flag's value on the ship (CrewId null) or on one crew member. A flag at 0 is not stored.
public sealed class StoredFlag
{
    public required string Flag { get; set; }

    public required string? CrewId { get; set; }

    public required int Value { get; set; }

    public required int SetAtWeek { get; set; }

    // The last encounter that changed it, or null when only answers have.
    public required string? EncounterId { get; set; }
}

// A storylet offered and not yet answered. SubjectId null: a storylet for the ship.
public sealed class WaitingStorylet
{
    public required string OfferId { get; set; }

    public required string StoryletId { get; set; }

    public required string? SubjectId { get; set; }

    public required string? EncounterId { get; set; }
}

// What the cycle whose aftermath last began did to the crew, for requirements on the last cycle (1e part two). Crew ids.
public sealed class LastCycleRecord
{
    public required int Cycle { get; set; }

    // Sent and came home.
    public required List<string> Sent { get; set; }

    public required List<string> Hurt { get; set; }

    public required List<string> Died { get; set; }
}

public sealed class StoryletsState
{
    [JsonRequired]
    public List<StoredFlag> Flags { get; set; } = new();

    // In the order offered.
    [JsonRequired]
    public List<WaitingStorylet> Waiting { get; set; } = new();

    // Null until a cycle's aftermath has begun, and again once the next orders open.
    public LastCycleRecord? LastCycle { get; set; }
}

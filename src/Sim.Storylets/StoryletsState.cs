using System.Text.Json.Serialization;

namespace Sim.Storylets;

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

// A vignette with a choice, arrived and not yet answered (1e part two).
public sealed class WaitingVignette
{
    public required string OfferId { get; set; }

    public required string VignetteId { get; set; }

    public required string? SubjectId { get; set; }

    public required string? Fact { get; set; }
}

// The last cycle a vignette arrived for one scope, for its cooldown.
public sealed class VignetteArrival
{
    public required string VignetteId { get; set; }

    public required string? SubjectId { get; set; }

    public required int Cycle { get; set; }
}

public sealed class StoryletsState
{
    // In the order offered.
    [JsonRequired]
    public List<WaitingStorylet> Waiting { get; set; } = new();

    // Null until a cycle's aftermath has begun, and again once the next orders open.
    public LastCycleRecord? LastCycle { get; set; }

    [JsonRequired]
    public List<WaitingVignette> Vignettes { get; set; } = new();

    [JsonRequired]
    public List<VignetteArrival> Arrivals { get; set; } = new();
}

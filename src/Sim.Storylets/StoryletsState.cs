using System.Text.Json.Serialization;

namespace Sim.Storylets;

// A storylet offered and not yet answered. SubjectId null: a storylet for the ship.
public sealed class WaitingStorylet
{
    public required string OfferId { get; set; }

    public required string StoryletId { get; set; }

    public required string? SubjectId { get; set; }

    // A pair's target (1e part three).
    public string? TargetId { get; set; }

    public required string? EncounterId { get; set; }
}

// One cycle of the history (1e part four, design 4.8): the kinds that happened to each living crew member in it, by crew id, in
// the order recorded. A crew member nothing happened to has no entry.
public sealed class HistoryCycle
{
    public required int Cycle { get; set; }

    public required Dictionary<string, List<string>> Crew { get; set; }
}

// A vignette with a choice, arrived and not yet answered (1e part two).
public sealed class WaitingVignette
{
    public required string OfferId { get; set; }

    public required string VignetteId { get; set; }

    public required string? SubjectId { get; set; }

    // A pair's target (1e part three).
    public string? TargetId { get; set; }

    public required string? Fact { get; set; }
}

// The last cycle a vignette arrived for one scope, for its cooldown.
public sealed class VignetteArrival
{
    public required string VignetteId { get; set; }

    public required string? SubjectId { get; set; }

    // A pair's target (1e part three): a vignette for a pair cools down for each pair apart.
    public string? TargetId { get; set; }

    public required int Cycle { get; set; }
}

public sealed class StoryletsState
{
    // In the order offered.
    [JsonRequired]
    public List<WaitingStorylet> Waiting { get; set; } = new();

    // The last history.cycles cycles, oldest first (1e part four). The dead drop out.
    [JsonRequired]
    public List<HistoryCycle> History { get; set; } = new();

    [JsonRequired]
    public List<WaitingVignette> Vignettes { get; set; } = new();

    [JsonRequired]
    public List<VignetteArrival> Arrivals { get; set; } = new();
}

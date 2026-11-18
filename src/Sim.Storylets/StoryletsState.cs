using System.Text.Json.Serialization;

namespace Sim.Storylets;

// A storylet offered and not yet answered. Cast: each slot to a crew member's id, in reading order; empty for a storylet for the
// ship (1e part four).
public sealed class WaitingStorylet
{
    public required string OfferId { get; set; }

    public required string StoryletId { get; set; }

    public required Dictionary<string, string> Cast { get; set; }

    public required string? EncounterId { get; set; }
}

// One cycle of the history (1e part four, design 4.8): the kinds that happened to each living crew member in it, by crew id, in
// the order recorded. A crew member nothing happened to has no entry.
public sealed class HistoryCycle
{
    public required int Cycle { get; set; }

    public required Dictionary<string, List<string>> Crew { get; set; }
}

// A vignette with a choice, arrived and not yet answered (1e part two). Cast: as a storylet's.
public sealed class WaitingVignette
{
    public required string OfferId { get; set; }

    public required string VignetteId { get; set; }

    public required Dictionary<string, string> Cast { get; set; }
}

// The last cycle a storylet or vignette arrived with one cast (1e part four, design 5.3): a vignette's cooldown is for one cast.
public sealed class Arrival
{
    public required string Id { get; set; }

    public required Dictionary<string, string> Cast { get; set; }

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

    // One entry for each storylet or vignette and cast, the latest. A cast with a member now dead is dropped.
    [JsonRequired]
    public List<Arrival> Arrivals { get; set; } = new();
}

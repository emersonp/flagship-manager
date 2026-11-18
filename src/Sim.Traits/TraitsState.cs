using System.Text.Json.Serialization;

namespace Sim.Traits;

// One trait's value on the ship (CrewId null), on one crew member, or on a pair (TargetId set). A trait at 0 is not stored.
public sealed class StoredTrait
{
    public required string Trait { get; set; }

    public required string? CrewId { get; set; }

    public required string? TargetId { get; set; }

    public required int Value { get; set; }

    // The week it last changed, by anything: both a definition's weekly step and its expiry count from here.
    public required int ChangedAtWeek { get; set; }

    // The last encounter that changed it, or null when nothing on a mission has.
    public required string? EncounterId { get; set; }
}

public sealed class TraitsState
{
    // In the order first set.
    [JsonRequired]
    public List<StoredTrait> Traits { get; set; } = new();
}

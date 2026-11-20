using System.Globalization;
using System.Text.Json.Serialization;
using Sim.Core;

namespace Sim.Chronicle;

// The chronicle (slice 1e part five, design 4.2): its entries in the order written, and the counter their ids are taken from. One
// module row, as every module's state is; a table of its own waits until the list is too long for one.
public sealed class ChronicleState
{
    [JsonRequired]
    public List<ChronicleEntry> Entries { get; set; } = new();

    // The last id given: the next entry is chr_ and this plus one, in four digits.
    [JsonRequired]
    public int Counter { get; set; }

    // The assignment of the site the ship is at, from SiteArrived, until the next; null before the first. A mission started there
    // takes it.
    [JsonRequired]
    public string? Site { get; set; }

    // Each mission from its start until its entry is written, for the facts its entries need (design 5.2).
    [JsonRequired]
    public List<OpenMission> Missions { get; set; } = new();

    public string NextId()
    {
        Counter++;
        return string.Create(CultureInfo.InvariantCulture, $"chr_{Counter:D4}");
    }
}

// A mission the chronicle is following: what its entries name (design 4.3), kept from MissionStarted until its own entry is written.
// The crew module publishes a mission's deaths after MissionEnded, so the end is held here (Ended, EndedSeq, Deaths) until each of
// its deaths has been written; then the mission's entry follows them.
public sealed class OpenMission
{
    [JsonRequired]
    public required string Id { get; set; }

    // The objective's template.
    [JsonRequired]
    public required string ObjectiveId { get; set; }

    // The site's assignment; null if the mission started before the chronicle heard of a site.
    [JsonRequired]
    public required string? SiteId { get; set; }

    [JsonRequired]
    public required List<string> Team { get; set; }

    // The lead the mission started with.
    [JsonRequired]
    public required string LeadId { get; set; }

    // The outcome's word (fullSuccess, loss, ...) once MissionEnded is heard; null while the mission runs.
    [JsonRequired]
    public string? Ended { get; set; }

    [JsonRequired]
    public long EndedSeq { get; set; }

    // How many of the team the result says died.
    [JsonRequired]
    public int Deaths { get; set; }

    // The sequence numbers of the deaths heard so far.
    [JsonRequired]
    public List<long> DeathSeqs { get; set; } = new();
}

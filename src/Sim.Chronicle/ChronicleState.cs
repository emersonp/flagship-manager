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

    public string NextId()
    {
        Counter++;
        return string.Create(CultureInfo.InvariantCulture, $"chr_{Counter:D4}");
    }
}

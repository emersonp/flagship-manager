using System.Text.Json.Serialization;

namespace Sim.Core.Content;

// The one place that knows how dice are rolled. A different mechanic replaces this type and nothing else.
public sealed record DiceDef(int Count, int Sides)
{
    [JsonIgnore]
    public double Average => Count * (Sides + 1) / 2.0;

    public int Roll(RngStream stream)
    {
        int total = 0;
        for (int i = 0; i < Count; i++)
        {
            total += 1 + stream.Next(Sides);
        }

        return total;
    }
}

using System.Text.Json.Serialization;

namespace Sim.Core.Content;

// The one place that knows how dice are rolled. A different mechanic replaces this type and nothing else.
public sealed record DiceDef(int Count, int Sides)
{
    [JsonIgnore]
    public double Average => Count * (Sides + 1) / 2.0;

    // The chance that a roll comes to at least this, worked out exactly.
    public double AtLeast(int needed)
    {
        // ways[t]: the number of ways the dice so far sum to t.
        var ways = new double[] { 1 };
        for (int i = 0; i < Count; i++)
        {
            var next = new double[ways.Length + Sides];
            for (int t = 0; t < ways.Length; t++)
            {
                for (int face = 1; face <= Sides; face++)
                {
                    next[t + face] += ways[t];
                }
            }

            ways = next;
        }

        return ways.Skip(Math.Max(needed, 0)).Sum() / Math.Pow(Sides, Count);
    }

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

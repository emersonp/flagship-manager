using System.Text;

namespace Sim.Core;

public sealed class RngStream
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    public RngStream(ulong state)
    {
        State = state;
    }

    public ulong State { get; private set; }

    public static RngStream For(ulong legacySeed, int cycle, string streamName)
    {
        ulong nameHash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(streamName))
        {
            nameHash ^= b;
            nameHash *= 1099511628211UL;
        }

        ulong state = Mix(legacySeed + Golden);
        state = Mix(state ^ (ulong)cycle);
        state = Mix(state ^ nameHash);
        return new RngStream(state);
    }

    public ulong NextUInt64()
    {
        State += Golden;
        return Mix(State);
    }

    public double NextDouble()
    {
        return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        }

        return (int)(NextDouble() * maxExclusive);
    }

    public int D6()
    {
        return 1 + Next(6);
    }

    public int TwoD6()
    {
        return D6() + D6();
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

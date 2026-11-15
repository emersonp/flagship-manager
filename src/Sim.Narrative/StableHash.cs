using System.Text;

namespace Sim.Narrative;

// FNV-1a over UTF-8: the same text gives the same number on every machine and run, unlike string.GetHashCode.
// Narrative picks come from this, never from an RNG stream, so text never disturbs the simulation's draws.
internal static class StableHash
{
    public static ulong Of(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    // An index in [0, count) for this key.
    public static int Pick(string key, int count)
    {
        return (int)(Of(key) % (ulong)count);
    }
}

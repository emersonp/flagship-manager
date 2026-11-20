using Sim.Core;

namespace Sim.Chronicle;

// Reads the chronicle as it stands today. A count's window is in days, today included, whatever the cycles were (design 4.6).
internal sealed class ChronicleView(ChronicleState state, int today) : IChronicleView
{
    public IReadOnlyList<ChronicleEntry> Entries => state.Entries;

    public int Count(string kind, int days)
    {
        return state.Entries.Count(e => e.Kind == kind && Within(e, days));
    }

    public int Count(string crewId, string kind, int days)
    {
        return state.Entries.Count(e => e.Kind == kind && Within(e, days) && e.Subjects.Any(s => s.Id == crewId));
    }

    private bool Within(ChronicleEntry entry, int days)
    {
        return entry.Day >= today - days + 1 && entry.Day <= today;
    }
}

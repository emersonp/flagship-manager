using Sim.Core;

namespace Sim.Storylets;

// The history (1e part four, design 4.8): for each living crew member, the kinds that happened to them in each of the last
// history.cycles cycles. Built from what the storylets module already hears: a mission's end, the aftermath, and the itinerary.
internal static class History
{
    // A mission is lost when its objective failed (lost, or not complete) or was abandoned (withdrawn). A death alone does not lose it.
    private static readonly MissionOutcome[] Lost = [MissionOutcome.Loss, MissionOutcome.Incomplete, MissionOutcome.Withdrawal];

    // Who came home, who was hurt, whether the mission was lost, and, when someone died on it, every other living crew member
    // lost a crewmate. The dead drop out.
    public static void MissionEnded(StoryletsState state, int cycle, MissionResult result, IReadOnlyList<string> roster)
    {
        HistoryCycle entry = Entry(state, cycle);
        HashSet<string> died = result.Crew.Where(c => c.Status == MemberStatus.Dead).Select(c => c.Id).ToHashSet();
        foreach (CrewResult member in result.Crew.Where(c => c.Status != MemberStatus.Dead))
        {
            Mark(entry, member.Id, HistoryKinds.Sent);
            if (member.Status == MemberStatus.Incapacitated || member.Injury > 0)
            {
                Mark(entry, member.Id, HistoryKinds.Hurt);
            }

            if (Lost.Contains(result.Outcome))
            {
                Mark(entry, member.Id, HistoryKinds.LostMission);
            }
        }

        if (died.Count > 0)
        {
            foreach (string crewId in roster.Where(id => !died.Contains(id)))
            {
                Mark(entry, crewId, HistoryKinds.LostCrewmate);
            }
        }

        foreach (string crewId in died)
        {
            Forget(state, crewId);
        }
    }

    // The aftermath began: the cycle is in the history even when no team was sent.
    public static void AftermathBegan(StoryletsState state, int cycle)
    {
        Entry(state, cycle);
    }

    // The next itinerary opened, so the last cycle's aftermath has ended: everyone fit who was not sent rested (fitness is read
    // now, after the week's recovery, as part two read it). Cycles older than the last `keep` fall out.
    public static void ItineraryOpened(StoryletsState state, int cycle, IRosterView roster, int keep)
    {
        if (state.History.FirstOrDefault(c => c.Cycle == cycle - 1) is { } last)
        {
            foreach (string crewId in roster.Roster.Where(id => roster.IsFit(id) && !Has(last, id, HistoryKinds.Sent)))
            {
                Mark(last, crewId, HistoryKinds.Rested);
            }
        }

        state.History.RemoveAll(c => c.Cycle < cycle - keep);
    }

    public static void Forget(StoryletsState state, string crewId)
    {
        foreach (HistoryCycle entry in state.History)
        {
            entry.Crew.Remove(crewId);
        }
    }

    // Whether any cycle before this one has ended. Part two's facts about the last cycle hold for no one, and are not turned
    // round by not, until one has.
    public static bool AnyEnded(StoryletsState state, int cycle)
    {
        return state.History.Any(c => c.Cycle == cycle - 1);
    }

    internal static bool Has(HistoryCycle entry, string crewId, string kind)
    {
        return entry.Crew.TryGetValue(crewId, out List<string>? kinds) && kinds.Contains(kind);
    }

    private static HistoryCycle Entry(StoryletsState state, int cycle)
    {
        if (state.History.FirstOrDefault(c => c.Cycle == cycle) is { } entry)
        {
            return entry;
        }

        entry = new HistoryCycle { Cycle = cycle, Crew = new() };
        state.History.Add(entry);
        return entry;
    }

    private static void Mark(HistoryCycle entry, string crewId, string kind)
    {
        if (!entry.Crew.TryGetValue(crewId, out List<string>? kinds))
        {
            entry.Crew[crewId] = kinds = [];
        }

        if (!kinds.Contains(kind))
        {
            kinds.Add(kind);
        }
    }
}

// The history as other modules read it, in the cycle `now`: the last cycle is the one before.
internal sealed class HistoryView(StoryletsState state, int now) : IHistoryView
{
    public int Count(string crewId, string kind, int cycles)
    {
        return state.History.Count(c => c.Cycle < now && c.Cycle >= now - cycles && History.Has(c, crewId, kind));
    }
}

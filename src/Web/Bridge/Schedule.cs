using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;

namespace Web.Bridge;

// The line at the top of the itinerary that says how the cycle's turn went (slice 1e-calendar, design 4.4): ahead of schedule,
// on schedule, or delayed, and for a journey, by how many days. Its words are schedule lines in the pack; with none that holds,
// fixed words. The cycle's length itself is never said, only how it compares with the days planned.
public static class Schedule
{
    public static string Kind(CycleLengthRolled rolled)
    {
        return rolled.Days < rolled.PlannedDays ? "ahead" : rolled.Days > rolled.PlannedDays ? "delayed" : "on";
    }

    // What a schedule line is chosen and expanded against. A journey is a turn planned for more than one week.
    public static Situation Situation(CalendarDef calendar, CycleLengthRolled rolled)
    {
        int off = Math.Abs(rolled.Days - rolled.PlannedDays);
        int weeks = rolled.PlannedDays / calendar.DaysPerWeek;
        var facts = new Dictionary<string, FactValue>
        {
            ["schedule"] = FactValue.Of(Kind(rolled)),
            ["days"] = FactValue.Of(off),
            ["weeks"] = FactValue.Of(weeks),
            ["travel"] = FactValue.Of(weeks > 1),
        };
        var words = new Dictionary<string, string>
        {
            ["days"] = Count(off, "day"),
            ["planned"] = Count(weeks, "week"),
        };
        return new Situation("schedule", facts, new Dictionary<string, Person>(), words);
    }

    // The line for the turn that began this cycle. The key is the legacy's seed and the cycle, so a reload shows the same words.
    public static IReadOnlyList<ProseEntry> Line(ContentPack pack, CycleLengthRolled rolled, ulong seed, int cycle)
    {
        Situation situation = Situation(pack.Calendar, rolled);
        string key = string.Create(CultureInfo.InvariantCulture, $"{seed}|schedule|{cycle}");
        if (Prose.Storylet(pack, situation, key) is { } prose)
        {
            return prose.Entries;
        }

        return [new ProseEntry(EntryKind.Narration, null, [new LinePart(false, Systems(situation))])];
    }

    private static string Systems(Situation situation)
    {
        string kind = situation.Facts["schedule"].Text!;
        if (!situation.Facts["travel"].Flag)
        {
            return kind switch
            {
                "ahead" => "The ship arrived ahead of schedule.",
                "delayed" => "The ship arrived delayed.",
                _ => "The ship arrived on schedule.",
            };
        }

        string journey = $"The journey, planned at {situation.Words["planned"]},";
        return kind switch
        {
            "ahead" => $"{journey} arrived {situation.Words["days"]} early.",
            "delayed" => $"{journey} arrived {situation.Words["days"]} late.",
            _ => $"{journey} arrived on schedule.",
        };
    }

    private static string Count(int n, string noun)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{n} {noun}{(n == 1 ? "" : "s")}");
    }
}

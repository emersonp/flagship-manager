namespace Sim.Core.Content;

// A calendar in content (content/calendars/; slice 1e-calendar, design 4.2). Short is the weekday's narrow form ("Stone").
public sealed record WeekdayDef(string Id, string Name, string Short);

public sealed record MonthDef(string Id, string Name, int Days);

// A date as written: Month is 0-based (an index into the calendar's months); Day is 1-based, as a date is written.
public readonly record struct CalendarDate(int Year, int Month, int Day);

// Start is the date a new legacy begins on. The week's length is the number of weekdays.
public sealed record CalendarDef(string Id, string Name, IReadOnlyList<WeekdayDef> Weekdays, IReadOnlyList<MonthDef> Months, CalendarDate Start)
{
    public int DaysPerWeek => Weekdays.Count;

    // The sum of the months.
    public int DaysPerYear => Months.Sum(m => m.Days);
}

// tuning/calendar.yaml: a cycle moves the calendar a whole number of days from CycleMinDays to CycleMaxDays (design 4.4).
public sealed record CalendarTuning(int CycleMinDays, int CycleMaxDays);

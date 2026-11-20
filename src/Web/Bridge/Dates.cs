using System.Globalization;
using Sim.Core;
using Sim.Core.Content;
using Calendar = Sim.Core.Calendar;

namespace Web.Bridge;

// The one formatter for dates and durations (slice 1e-calendar, design 4.7). A date is short everywhere ("13 Vortun 105") but
// on hover and where it stands alone as a heading ("Stoneday, 13 Vortun 105"; narrow screens "Stone, 13 Vortun 105"). A duration
// is in weeks, rounded to the nearest; no screen in play shows a day number or a week number.
public static class Dates
{
    public static string Short(CalendarDef calendar, int day)
    {
        CalendarDate date = Calendar.Date(calendar, day);
        return string.Create(CultureInfo.InvariantCulture, $"{date.Day} {calendar.Months[date.Month].Name} {date.Year}");
    }

    public static string Full(CalendarDef calendar, int day)
    {
        return $"{Calendar.Weekday(calendar, day).Name}, {Short(calendar, day)}";
    }

    public static string FullNarrow(CalendarDef calendar, int day)
    {
        return $"{Calendar.Weekday(calendar, day).Short}, {Short(calendar, day)}";
    }

    // Nearest week, a half week rounding up, but anything up to half a week is "under a week" (with 6-day weeks: under 4
    // days; 4 to 8 days, 1 week; 9 to 14, 2 weeks). Something already due reads as under a week.
    public static string Weeks(CalendarDef calendar, int days)
    {
        int perWeek = calendar.DaysPerWeek;
        if (days * 2 <= perWeek)
        {
            return "under a week";
        }

        int weeks = ((days * 2) + perWeek) / (perWeek * 2);
        return weeks == 1 ? "1 week" : string.Create(CultureInfo.InvariantCulture, $"{weeks} weeks");
    }
}

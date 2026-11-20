using Sim.Core.Content;

namespace Sim.Core;

// The calendar's arithmetic (slice 1e-calendar, design 4.2 and 4.3). The engine keeps time as a day number: day 0 is the 1st of
// the first month of year 1, and the first weekday. A day before it (a birth date, later) is negative and still converts.
public static class Calendar
{
    public static int DayNumber(CalendarDef calendar, CalendarDate date)
    {
        if (date.Month < 0 || date.Month >= calendar.Months.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, $"The calendar {calendar.Id} has no month {date.Month}.");
        }

        if (date.Day < 1 || date.Day > calendar.Months[date.Month].Days)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, $"{calendar.Months[date.Month].Id} has no day {date.Day}.");
        }

        int before = 0;
        for (int m = 0; m < date.Month; m++)
        {
            before += calendar.Months[m].Days;
        }

        return ((date.Year - 1) * calendar.DaysPerYear) + before + date.Day - 1;
    }

    public static CalendarDate Date(CalendarDef calendar, int day)
    {
        int perYear = calendar.DaysPerYear;
        int year = (int)Math.Floor((double)day / perYear);
        int rest = day - (year * perYear);
        int month = 0;
        while (rest >= calendar.Months[month].Days)
        {
            rest -= calendar.Months[month].Days;
            month++;
        }

        return new CalendarDate(year + 1, month, rest + 1);
    }

    public static WeekdayDef Weekday(CalendarDef calendar, int day)
    {
        int n = calendar.DaysPerWeek;
        return calendar.Weekdays[((day % n) + n) % n];
    }

    // A duration written in weeks (content and tuning write only weeks), in days.
    public static int Days(CalendarDef calendar, int weeks)
    {
        return weeks * calendar.DaysPerWeek;
    }
}

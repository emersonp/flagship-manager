using Sim.Core;
using Sim.Core.Content;

namespace Sim.Tests;

// The calendar's arithmetic (slice 1e-calendar, design 4.2 and 4.3), on the Bastion Calendar as the lore writes it.
public class CalendarTests
{
    private static readonly string[] MonthNames = ["Krellis", "Vortun", "Zhortuna", "Moss", "Pharoska", "Pelpheär", "Barx", "Bast", "Venjenten", "Almonten", "Orphelten", "Othra"];

    private static readonly CalendarDef Bastion = new(
        "bastion",
        "Bastion Calendar",
        [
            new("stoneday", "Stoneday", "Stone"), new("quorday", "Quorday", "Quor"), new("kinclause", "Kinclause", "Kinclause"),
            new("embertally", "Embertally", "Ember"), new("menday", "Menday", "Menday"), new("hearthfall", "Hearthfall", "Hearth"),
        ],
        MonthNames.Select(n => new MonthDef(n.ToLowerInvariant().Replace('ä', 'a'), n, 30)).ToList(),
        new CalendarDate(105, 1, 13));

    [Fact]
    public void The_legacy_starts_on_day_37482_a_Stoneday()
    {
        int day = Calendar.DayNumber(Bastion, Bastion.Start);

        Assert.Equal(37482, day);
        Assert.Equal("stoneday", Calendar.Weekday(Bastion, day).Id);
        Assert.Equal(new CalendarDate(105, 1, 13), Calendar.Date(Bastion, 37482));
    }

    [Fact]
    public void Day_zero_is_the_first_of_the_first_month_of_year_one()
    {
        Assert.Equal(0, Calendar.DayNumber(Bastion, new CalendarDate(1, 0, 1)));
        Assert.Equal(new CalendarDate(1, 0, 1), Calendar.Date(Bastion, 0));
        Assert.Equal("stoneday", Calendar.Weekday(Bastion, 0).Id);
    }

    [Theory]
    [InlineData(104, 11, 30)]
    [InlineData(105, 0, 1)]
    [InlineData(105, 1, 30)]
    [InlineData(105, 2, 1)]
    [InlineData(105, 11, 30)]
    public void A_date_survives_the_round_trip(int year, int month, int day)
    {
        var date = new CalendarDate(year, month, day);

        Assert.Equal(date, Calendar.Date(Bastion, Calendar.DayNumber(Bastion, date)));
    }

    [Fact]
    public void The_last_day_of_a_year_is_followed_by_the_first_of_the_next()
    {
        int last = Calendar.DayNumber(Bastion, new CalendarDate(104, 11, 30));

        Assert.Equal(new CalendarDate(105, 0, 1), Calendar.Date(Bastion, last + 1));
        Assert.Equal(360, Bastion.DaysPerYear);
    }

    [Fact]
    public void Every_first_of_a_Bastion_month_is_a_Stoneday()
    {
        foreach (int year in new[] { 1, 104, 105, 106 })
        {
            for (int month = 0; month < 12; month++)
            {
                Assert.Equal("stoneday", Calendar.Weekday(Bastion, Calendar.DayNumber(Bastion, new CalendarDate(year, month, 1))).Id);
            }
        }
    }

    [Fact]
    public void Weeks_are_the_calendars_weekdays_long()
    {
        Assert.Equal(6, Bastion.DaysPerWeek);
        Assert.Equal(12, Calendar.Days(Bastion, 2));
        Assert.Equal(0, Calendar.Days(Bastion, 0));
    }

    // Months of unequal length: the arithmetic walks the months, not a fixed 30.
    [Fact]
    public void Uneven_months_count_by_their_own_lengths()
    {
        var odd = new CalendarDef("odd", "Odd", [new("a", "A", "A"), new("b", "B", "B")], [new("m", "M", 3), new("n", "N", 5)], new CalendarDate(1, 0, 1));

        Assert.Equal(8, odd.DaysPerYear);
        Assert.Equal(3, Calendar.DayNumber(odd, new CalendarDate(1, 1, 1)));
        Assert.Equal(new CalendarDate(1, 1, 5), Calendar.Date(odd, 7));
        Assert.Equal(new CalendarDate(2, 0, 1), Calendar.Date(odd, 8));
        Assert.Equal("b", Calendar.Weekday(odd, 3).Id);
    }

    [Fact]
    public void A_date_outside_the_calendar_has_no_day_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.DayNumber(Bastion, new CalendarDate(105, 12, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.DayNumber(Bastion, new CalendarDate(105, 0, 31)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.DayNumber(Bastion, new CalendarDate(105, 0, 0)));
    }
}

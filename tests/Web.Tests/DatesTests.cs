using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Web.Bridge;

namespace Web.Tests;

// The one place a day number becomes words (slice 1e-calendar, Task 5; design 4.7): dates short and full, and durations in
// weeks rounded to the nearest, on the game's own Bastion Calendar.
public class DatesTests
{
    private static readonly CalendarDef Bastion = ContentLoader.LoadDirectory(Path.Combine(TestApp.RepositoryRoot(), "content")).Pack!.Calendar;

    [Theory]
    [InlineData(0, "under a week")]
    [InlineData(1, "under a week")]
    [InlineData(3, "under a week")]
    [InlineData(4, "1 week")]
    [InlineData(6, "1 week")]
    [InlineData(8, "1 week")]
    [InlineData(9, "2 weeks")]
    [InlineData(14, "2 weeks")]
    [InlineData(15, "3 weeks")]
    [InlineData(-2, "under a week")]
    public void A_duration_is_the_nearest_week(int days, string expected)
    {
        Assert.Equal(expected, Dates.Weeks(Bastion, days));
    }

    [Fact]
    public void The_start_day_reads_13_Vortun_105_a_Stoneday()
    {
        Assert.Equal("13 Vortun 105", Dates.Short(Bastion, 37482));
        Assert.Equal("Stoneday, 13 Vortun 105", Dates.Full(Bastion, 37482));
        Assert.Equal("Stone, 13 Vortun 105", Dates.FullNarrow(Bastion, 37482));
    }

    [Fact]
    public void The_last_day_of_a_year_is_30_Othra_and_the_next_is_1_Krellis()
    {
        int last = Calendar.DayNumber(Bastion, new CalendarDate(105, 11, 30));

        Assert.Equal("30 Othra 105", Dates.Short(Bastion, last));
        Assert.Equal("Hearthfall, 30 Othra 105", Dates.Full(Bastion, last));
        Assert.Equal("Hearth, 30 Othra 105", Dates.FullNarrow(Bastion, last));
        Assert.Equal("Stoneday, 1 Krellis 106", Dates.Full(Bastion, last + 1));
    }

    [Fact]
    public void A_month_keeps_its_written_name()
    {
        Assert.Equal("1 Pelpheär 105", Dates.Short(Bastion, Calendar.DayNumber(Bastion, new CalendarDate(105, 5, 1))));
    }
}

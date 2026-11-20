using System.Net;
using System.Text.RegularExpressions;
using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Web.Bridge;

namespace Web.Tests;

// The line at the top of the itinerary that says how the cycle's turn went (slice 1e-calendar, Task 3): ahead of schedule, on
// schedule, or delayed, and for a journey by how many days, from the pack's schedule lines or, with none, fixed words.
public sealed class ScheduleTests : IDisposable
{
    private static readonly ContentPack Pack = BridgePack.Load();

    private readonly TestApp app = new();
    private readonly HttpClient client;

    public ScheduleTests()
    {
        client = app.NewClient();
    }

    public void Dispose()
    {
        app.Dispose();
    }

    private static string Said(ContentPack pack, int days, int planned, int cycle = 2)
    {
        return string.Concat(Schedule.Line(pack, new CycleLengthRolled(days, planned), 1, cycle).SelectMany(e => e.Parts).Select(p => p.Text));
    }

    [Theory]
    [InlineData(5, 6, "SCHEDULE_AHEAD.")]
    [InlineData(6, 6, "SCHEDULE_ON.")]
    [InlineData(7, 6, "SCHEDULE_DELAYED.")]
    [InlineData(25, 30, "SCHEDULE_TRAVEL_EARLY 5 weeks, 5 days.")]
    [InlineData(35, 30, "SCHEDULE_TRAVEL_LATE 5 weeks, 5 days.")]
    [InlineData(14, 12, "SCHEDULE_TRAVEL_LATE 2 weeks, 2 days.")]
    [InlineData(30, 30, "SCHEDULE_ON.")]
    public void The_packs_line_says_how_the_turn_went(int days, int planned, string expected)
    {
        Assert.Equal(expected, Said(Pack, days, planned));
    }

    [Theory]
    [InlineData(5, 6, "The ship arrived ahead of schedule.")]
    [InlineData(6, 6, "The ship arrived on schedule.")]
    [InlineData(7, 6, "The ship arrived delayed.")]
    [InlineData(25, 30, "The journey, planned at 5 weeks, arrived 5 days early.")]
    [InlineData(35, 30, "The journey, planned at 5 weeks, arrived 5 days late.")]
    [InlineData(31, 30, "The journey, planned at 5 weeks, arrived 1 day late.")]
    [InlineData(30, 30, "The journey, planned at 5 weeks, arrived on schedule.")]
    public void With_no_schedule_line_fixed_words_say_it(int days, int planned, string expected)
    {
        Assert.Equal(expected, Said(Pack with { Lines = Pack.Lines.Where(l => l.Beat != "schedule").ToList() }, days, planned));
    }

    // The game's own words: one per case, each its own line, and never the cycle's length as a figure.
    [Theory]
    [InlineData(5, 6, "LINE_SCHEDULE_AHEAD")]
    [InlineData(6, 6, "LINE_SCHEDULE_ON")]
    [InlineData(7, 6, "LINE_SCHEDULE_DELAYED")]
    [InlineData(25, 30, "LINE_SCHEDULE_TRAVEL_AHEAD")]
    [InlineData(30, 30, "LINE_SCHEDULE_TRAVEL_ON")]
    [InlineData(35, 30, "LINE_SCHEDULE_TRAVEL_DELAYED")]
    public void The_games_pack_has_a_line_for_every_case(int days, int planned, string line)
    {
        ContentPack game = Game();
        ContentPack only = game with { Lines = game.Lines.Where(l => l.Id == line).ToList() };

        string said = Said(game, days, planned);

        Assert.Equal(Said(only, days, planned), said);
        Assert.DoesNotContain(days.ToString(System.Globalization.CultureInfo.InvariantCulture), said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_journey_line_names_the_days_early_or_late()
    {
        Assert.Equal("Planned at 5 weeks, the journey came in 5 days early.", Said(Game(), 25, 30));
        Assert.Equal("Planned at 2 weeks, the journey ran 2 days late.", Said(Game(), 14, 12));
    }

    // Through the real app: the itinerary after a turn says what the turn's roll was, and a page drawn again from the store says
    // the same; the first itinerary, which no turn began, says nothing.
    [Fact]
    public async Task The_itinerary_after_a_turn_says_how_it_went_and_a_reload_says_the_same()
    {
        Guid id = await app.Sessions.CreateLegacy(app.Live, "LEGACY", 1);
        string first = await Page(id);
        Assert.Contains("Captain's itinerary", first, StringComparison.Ordinal);
        Assert.Empty(Line(first));

        await To(id, CyclePhase.Advance);
        HttpResponseMessage advanced = await client.PostAsync($"/legacy/{id}/advance", new FormUrlEncodedContent(app.Stamped(id, [])));
        Assert.Equal(HttpStatusCode.OK, advanced.StatusCode);
        string patched = Line(WebUtility.HtmlDecode(await advanced.Content.ReadAsStringAsync()));

        CycleLengthRolled rolled = Rolled(id, 1);
        string drawn = Line(await Page(id));
        Assert.Contains($"SCHEDULE_{Schedule.Kind(rolled).ToUpperInvariant()}.", drawn, StringComparison.Ordinal);
        Assert.Equal(drawn, Line(await Page(id)));
        Assert.Equal(drawn, patched);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task A_journey_says_the_days_early_or_late(ulong seed)
    {
        Guid id = await app.CreateAtOrders("LEGACY", seed);
        await To(id, CyclePhase.Advance);
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            legacy.Send(new Travel(5));
            return legacy;
        });

        CycleLengthRolled rolled = Rolled(id, 1);
        string drawn = Line(await Page(id));
        Assert.Equal(30, rolled.PlannedDays);
        Assert.Contains(Schedule.Kind(rolled) switch
        {
            "ahead" => "SCHEDULE_TRAVEL_EARLY 5 weeks, 5 days.",
            "delayed" => "SCHEDULE_TRAVEL_LATE 5 weeks, 5 days.",
            _ => "SCHEDULE_ON.",
        }, drawn, StringComparison.Ordinal);
    }

    private static ContentPack Game()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return ContentLoader.LoadDirectory(Path.Combine(directory!.FullName, "content")).Pack!;
    }

    private async Task<string> Page(Guid id)
    {
        return WebUtility.HtmlDecode(await client.GetStringAsync($"/legacy/{id}"));
    }

    private static string Line(string page)
    {
        return Regex.Match(page, "<div id=\"schedule\".*?</div>\\s*</div>", RegexOptions.Singleline).Value;
    }

    private async Task To(Guid id, CyclePhase phase)
    {
        await app.Sessions.Run(app.Live, id, legacy =>
        {
            while (legacy.State.Phase != phase)
            {
                legacy.Send(new AdvancePhase());
            }

            return legacy;
        });
    }

    // The roll the turn out of this cycle made, from the stored log.
    private CycleLengthRolled Rolled(Guid id, int cycle)
    {
        return app.Game.Store.ReadEvents(id, cycle).Select(app.Live.Game.Events.FromEnvelope).OfType<CycleLengthRolled>().Single();
    }
}

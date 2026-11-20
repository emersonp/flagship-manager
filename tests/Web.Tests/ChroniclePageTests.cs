using System.Net;
using System.Text.RegularExpressions;
using Persistence;
using Sim.Core;
using Sim.Core.Content;
using Sim.Missions;
using Web.Bridge;

namespace Web.Tests;

// The Chronicle page (1e part five, Task 6; design 4.7): newest first under full dates, All or Notable, opened from the top bar in any
// phase without changing it, and the dev figures in Dev Play alone.
public sealed partial class ChroniclePageTests
{
    private static readonly ContentPack Pack = BridgePack.Load();

    [GeneratedRegex(@"<h2 id=""day-(\d+)""|<li class=""entry[^""]*"">.*?<p class=""entry__text"">(.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex DaysAndEntries();

    private static ChronicleEntry Entry(string id, int day, string kind, int weight, string text, params long[] seq)
    {
        return new ChronicleEntry(id, day, 1, kind, weight, kind == ChronicleKinds.Death ? [new ChronicleSubject("CREW_02", "NAME_02")] : [], text, seq);
    }

    private static LegacyRecord Record()
    {
        return new LegacyRecord(new LegacyState { LegacyId = Guid.NewGuid(), Seed = 1, Day = 37490 }, Pack.Id, Pack.Version, "LEGACY", DateTimeOffset.UnixEpoch);
    }

    // The days and the entries' texts in the order the page draws them: a day as "day N", an entry as its text.
    private static List<string> Drawn(string html)
    {
        return DaysAndEntries().Matches(html).Select(m => m.Groups[1].Success ? $"day {m.Groups[1].Value}" : WebUtility.HtmlDecode(m.Groups[2].Value)).ToList();
    }

    // Plays a fixture legacy through the web until the condition holds, after at least one mission has ended.
    private static async Task<(TestApp App, HttpClient Client, Guid Id, WebCaptain Captain)> Played(Func<LoadedLegacy, ICommand, bool> until, bool developerMode = false)
    {
        var app = new TestApp(developerMode: developerMode, scratch: developerMode);
        HttpClient client = app.NewClient();
        Guid id = await app.CreateAtOrders("LEGACY_ONE", 3);
        var captain = new WebCaptain(app, client, id);
        for (int steps = 0; steps < 400; steps++)
        {
            LoadedLegacy legacy = await captain.Read();
            if (until(legacy, await captain.Peek()))
            {
                return (app, client, id, captain);
            }

            await captain.Step();
        }

        app.Dispose();
        throw new InvalidOperationException("The legacy never reached the wanted state.");
    }

    private static IReadOnlyList<ChronicleEntry> Entries(TestApp app, LoadedLegacy legacy)
    {
        return app.Live.Game.Bus.View<IChronicleView>(legacy.State).Entries;
    }

    [Fact]
    public void Entries_are_newest_first_under_their_dates_and_a_mission_sits_above_its_deaths()
    {
        // Written in this order: the founding; on day 37486, a call, then two deaths, then the mission they ended (its end's
        // sequence number first, as the chronicle records it).
        ChronicleEntry[] entries =
        [
            Entry("chr_0001", 37482, ChronicleKinds.Founding, 5, "FOUNDED", 1),
            Entry("chr_0002", 37486, ChronicleKinds.Call, 3, "CALL", 20),
            Entry("chr_0003", 37486, ChronicleKinds.Death, 5, "DEATH_ONE", 31),
            Entry("chr_0004", 37486, ChronicleKinds.Death, 5, "DEATH_TWO", 32),
            Entry("chr_0005", 37486, ChronicleKinds.Mission, 2, "MISSION", 30, 31, 32),
        ];

        ChronicleView view = BridgeReader.Chronicle(Record(), Pack, entries, notable: false);

        Assert.Equal([37486, 37482], view.Days.Select(d => d.Day));
        Assert.Equal(["MISSION", "DEATH_TWO", "DEATH_ONE", "CALL"], view.Days[0].Entries.Select(e => e.Text));
        Assert.Equal(Dates.Full(Pack.Calendar, 37486), view.Days[0].Date);
        Assert.Equal(Dates.FullNarrow(Pack.Calendar, 37486), view.Days[0].DateNarrow);
        Assert.True(view.Days[0].Entries[1].Memorial);
        Assert.Equal(["NAME_02"], view.Days[0].Entries[1].Subjects);
        Assert.Equal([false, true, true, false], view.Days[0].Entries.Select(e => e.SetApart));

        ChronicleView notable = BridgeReader.Chronicle(Record(), Pack, entries, notable: true);

        Assert.Equal(["DEATH_TWO", "DEATH_ONE", "CALL"], notable.Days[0].Entries.Select(e => e.Text));
        Assert.Equal(5, notable.Total);
    }

    [Fact]
    public async Task The_page_lists_a_legacys_entries_in_order_under_their_dates()
    {
        (TestApp app, HttpClient client, Guid id, WebCaptain captain) = await Played((legacy, _) => legacy.State.Cycle >= 3);
        using (app)
        {
            LoadedLegacy legacy = await captain.Read();
            IReadOnlyList<ChronicleEntry> entries = Entries(app, legacy);
            string page = await client.GetStringAsync($"/legacy/{id}/chronicle");
            List<string> drawn = Drawn(page);

            // Every day once, newest first, each heading its own entries; within a day by the last event recorded, newest first.
            List<string> expected = entries
                .Select((e, written) => (Entry: e, Written: written))
                .GroupBy(e => e.Entry.Day)
                .OrderByDescending(g => g.Key)
                .SelectMany(g => g.OrderByDescending(e => e.Entry.SourceSeq.Max()).ThenByDescending(e => e.Written).Select(e => e.Entry.Text).Prepend($"day {g.Key}"))
                .ToList();
            Assert.Contains(entries, e => e.Kind == ChronicleKinds.Mission);
            Assert.Contains(entries, e => e.Kind == ChronicleKinds.Call);
            Assert.Equal(expected, drawn);
            Assert.StartsWith("CHRONICLE_FOUNDING", drawn[^1], StringComparison.Ordinal);
            foreach (int day in entries.Select(e => e.Day).Distinct())
            {
                Assert.Contains($"<span class=\"chronicle-day__full\">{WebUtility.HtmlEncode(Dates.Full(app.Live.Pack.Calendar, day))}</span>", page);
            }

            Assert.Contains("<a class=\"btn\" href=\"/legacy/" + id + "/chronicle\" aria-current=\"page\">All</a>", page);
            Assert.DoesNotContain("entry__dev", page);
        }
    }

    [Fact]
    public async Task Notable_hides_weight_1_and_2()
    {
        (TestApp app, HttpClient client, Guid id, WebCaptain captain) = await Played((legacy, _) => legacy.State.Cycle >= 3);
        using (app)
        {
            IReadOnlyList<ChronicleEntry> entries = Entries(app, await captain.Read());
            string all = await client.GetStringAsync($"/legacy/{id}/chronicle");
            string notable = await client.GetStringAsync($"/legacy/{id}/chronicle?show=notable");

            Assert.Contains(entries, e => e.Weight < 3);
            Assert.Equal(entries.Count, Regex.Count(all, "<li class=\"entry"));
            Assert.Equal(entries.Count(e => e.Weight >= 3), Regex.Count(notable, "<li class=\"entry"));
            Assert.Equal(entries.Count(e => e.Weight >= 4), Regex.Count(notable, "<li class=\"entry entry--apart"));
            Assert.Contains("aria-current=\"page\">Notable</a>", notable);
            Assert.DoesNotContain("aria-current=\"page\">All</a>", notable);
        }
    }

    [Fact]
    public async Task Opened_with_a_call_waiting_it_changes_nothing_and_the_bridge_returns_to_the_call()
    {
        (TestApp app, HttpClient client, Guid id, WebCaptain captain) = await Played((legacy, next) => next is AnswerCall);
        using (app)
        {
            LoadedLegacy before = await captain.Read();
            int stored = app.Game.Store.ReadEvents(id, before.State.Cycle).Count;

            HttpResponseMessage response = await client.GetAsync($"/legacy/{id}/chronicle");
            string page = await response.Content.ReadAsStringAsync();
            string bridge = await client.GetStringAsync($"/legacy/{id}");
            LoadedLegacy after = await captain.Read();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"<a class=\"home\" href=\"/legacy/{id}\">Bridge</a>", page);
            Assert.Contains("aria-current=\"step\"", page);
            Assert.Equal((before.State.Cycle, before.State.Phase, before.State.Day), (after.State.Cycle, after.State.Phase, after.State.Day));
            Assert.Equal(stored, app.Game.Store.ReadEvents(id, after.State.Cycle).Count);
            Assert.Equal(CyclePhase.Resolution, after.State.Phase);
            Assert.Contains("name=\"call\"", bridge);
            Assert.Contains($"<a class=\"home\" href=\"/legacy/{id}/chronicle\">Chronicle</a>", bridge);
            Assert.IsType<AnswerCall>(await captain.Peek());
        }
    }

    [Fact]
    public async Task Dev_play_shows_each_entrys_kind_weight_cycle_and_sequence_numbers()
    {
        (TestApp app, HttpClient client, Guid id, WebCaptain captain) = await Played((legacy, _) => legacy.State.Cycle >= 2, developerMode: true);
        using (app)
        {
            ChronicleEntry founding = Entries(app, await captain.Read())[0];
            string dev = await client.GetStringAsync($"/dev/legacy/{id}/chronicle");
            string bridge = await client.GetStringAsync($"/dev/legacy/{id}");

            Assert.Contains($"<div><dt>Kind</dt><dd>founding</dd></div><div><dt>Weight</dt><dd class=\"m\">5</dd></div><div><dt>Cycle</dt><dd class=\"m\">{founding.Cycle}</dd></div><div><dt>Seq</dt><dd class=\"m\">{string.Join(", ", founding.SourceSeq)}</dd></div>", dev);
            Assert.Contains($"<a class=\"home\" href=\"/dev/legacy/{id}\">Bridge</a>", dev);
            Assert.Contains($"href=\"/dev/legacy/{id}/chronicle?show=notable\"", dev);
            Assert.Contains($"<a class=\"home\" href=\"/dev/legacy/{id}/chronicle\">Chronicle</a>", bridge);
        }
    }

    [Fact]
    public async Task A_legacy_that_does_not_exist_is_not_found()
    {
        using var app = new TestApp();

        HttpResponseMessage response = await app.NewClient().GetAsync($"/legacy/{Guid.NewGuid()}/chronicle");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

using Sim.Chronicle;
using Sim.Core;
using Sim.Core.Content;
using Sim.Core.Narrative;
using Sim.Crew;
using Sim.Cycle;
using Sim.Narrative;

namespace Sim.Tests;

// Slice 1e part five: the chronicle module, its narrator, and its view.
public class ChronicleModuleTests
{
    // 13 MONTH_B 105, the fixture calendar's start.
    private const int StartDay = 37482;

    private static EventBus NewBus(ContentPack pack)
    {
        var bus = new EventBus();
        CycleModule.Register(bus, pack);
        CrewModule.Register(bus, pack);
        ChronicleModule.Register(bus, pack, new Narrator(pack));
        return bus;
    }

    private static (LegacyState State, IReadOnlyList<PublishedEvent> Published) Found(ContentPack pack, ulong seed = 1)
    {
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = seed };
        IReadOnlyList<PublishedEvent> published = NewBus(pack).Send(state, new FoundLegacy());
        return (state, published);
    }

    private static Situation Founding(int crew)
    {
        return new Situation(
            NarrativeFacts.Chronicle.Founding,
            new Dictionary<string, FactValue> { ["crew"] = FactValue.Of(crew) },
            new Dictionary<string, Person>(),
            new Dictionary<string, string> { ["crew"] = crew.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private static LineDef Line(string id, string text, params CriterionDef[] when)
    {
        return new LineDef(id, NarrativeFacts.Chronicle.Founding, when, text);
    }

    [Fact]
    public void The_narrator_expands_the_line_that_holds()
    {
        ContentPack pack = TestContent.Pack with { Lines = [Line("LINE_A", "A crew of #crew#."), Line("LINE_B", "A crew of eight.", new CriterionDef("crew", ["8"]))] };

        var narrator = new Narrator(pack);

        Assert.Equal("A crew of eight.", narrator.Line(Founding(8), "key"));
        Assert.Equal("A crew of 3.", narrator.Line(Founding(3), "key"));
    }

    [Fact]
    public void The_narrator_gives_null_when_no_line_holds()
    {
        ContentPack pack = TestContent.Pack with { Lines = [Line("LINE_B", "A crew of eight.", new CriterionDef("crew", ["8"]))] };

        Assert.Null(new Narrator(pack).Line(Founding(3), "key"));
    }

    // Ties break by the key alone: no line is passed over for having been used, so asking again gives the same words.
    [Fact]
    public void The_narrator_gives_the_same_line_for_the_same_key()
    {
        ContentPack pack = TestContent.Pack with { Lines = Enumerable.Range(1, 6).Select(i => Line($"LINE_{i}", $"WORDS_{i}.")).ToList() };
        var narrator = new Narrator(pack);

        List<string?> first = Enumerable.Range(1, 20).Select(i => narrator.Line(Founding(8), $"key|{i}")).ToList();
        List<string?> again = Enumerable.Range(1, 20).Select(i => narrator.Line(Founding(8), $"key|{i}")).ToList();

        Assert.Equal(first, again);
        Assert.True(first.Distinct().Count() > 1, "different keys should spread over the tied lines");
    }

    [Fact]
    public void The_founding_writes_one_entry_with_its_day_and_frozen_words()
    {
        (LegacyState state, IReadOnlyList<PublishedEvent> published) = Found(TestContent.Pack);
        long founded = published.Single(p => p.Event is LegacyFounded).Sequence;

        ChronicleEntry entry = Assert.Single(state.Module<ChronicleState>(ChronicleModule.Name).Entries);

        Assert.Equal(("chr_0001", StartDay, 1, ChronicleKinds.Founding, 5, "CHRONICLE_FOUNDING 8."), (entry.Id, entry.Day, entry.Cycle, entry.Kind, entry.Weight, entry.Text));
        Assert.Empty(entry.Subjects);
        Assert.Equal([founded], entry.SourceSeq);
    }

    [Fact]
    public void With_no_line_the_founding_falls_back_to_fixed_words()
    {
        (LegacyState state, _) = Found(TestContent.Pack with { Lines = [] });

        Assert.Equal(ChronicleModule.FoundingFallback, Assert.Single(state.Module<ChronicleState>(ChronicleModule.Name).Entries).Text);
    }

    [Fact]
    public void A_weight_of_0_writes_no_entry_and_spends_no_id()
    {
        var weights = new Dictionary<string, int>(ChronicleTuning.Default.Weights) { [ChronicleTuning.Founding] = 0 };

        (LegacyState state, _) = Found(TestContent.Pack with { Chronicle = new ChronicleTuning(weights, 3) });

        ChronicleState chronicle = state.Module<ChronicleState>(ChronicleModule.Name);
        Assert.Empty(chronicle.Entries);
        Assert.Equal(0, chronicle.Counter);
    }

    // The window is days, today included: the day `days - 1` back counts and the day before it does not.
    [Fact]
    public void Count_holds_today_and_the_days_before_it_within_the_window()
    {
        EventBus bus = NewBus(TestContent.Pack);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1, Day = 1000 };
        state.Module<ChronicleState>(ChronicleModule.Name).Entries.AddRange(
        [
            Entry("chr_0001", 1000, ChronicleKinds.Death),
            Entry("chr_0002", 881, ChronicleKinds.Death),
            Entry("chr_0003", 880, ChronicleKinds.Death),
            Entry("chr_0004", 1000, ChronicleKinds.Mission),
        ]);

        IChronicleView view = bus.View<IChronicleView>(state);

        Assert.Equal(2, view.Count(ChronicleKinds.Death, 120));
        Assert.Equal(3, view.Count(ChronicleKinds.Death, 121));
        Assert.Equal(1, view.Count(ChronicleKinds.Death, 1));
        Assert.Equal(1, view.Count(ChronicleKinds.Mission, 1));
        Assert.Equal(0, view.Count(ChronicleKinds.Call, 1000));
        Assert.Equal(["chr_0001", "chr_0002", "chr_0003", "chr_0004"], view.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Count_for_a_crew_member_reads_only_the_entries_they_are_a_subject_of()
    {
        EventBus bus = NewBus(TestContent.Pack);
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1, Day = 1000 };
        state.Module<ChronicleState>(ChronicleModule.Name).Entries.AddRange(
        [
            Entry("chr_0001", 990, ChronicleKinds.Call, "CREW_01"),
            Entry("chr_0002", 995, ChronicleKinds.Call, "CREW_02"),
            Entry("chr_0003", 999, ChronicleKinds.Storylet, "CREW_02", "CREW_01"),
            Entry("chr_0004", 900, ChronicleKinds.Call, "CREW_01"),
        ]);

        IChronicleView view = bus.View<IChronicleView>(state);

        Assert.Equal(1, view.Count("CREW_01", ChronicleKinds.Call, 60));
        Assert.Equal(2, view.Count("CREW_01", ChronicleKinds.Call, 101));
        Assert.Equal(1, view.Count("CREW_01", ChronicleKinds.Storylet, 60));
        Assert.Equal(0, view.Count("CREW_03", ChronicleKinds.Call, 1000));
        Assert.Equal(3, view.Count(ChronicleKinds.Call, 101));
    }

    // A view reads without writing: an unfounded legacy has no chronicle, and reading one leaves it so.
    [Fact]
    public void Reading_the_view_creates_no_state()
    {
        var state = new LegacyState { LegacyId = Guid.Empty, Seed = 1 };

        Assert.Empty(NewBus(TestContent.Pack).View<IChronicleView>(state).Entries);
        Assert.False(state.Modules.ContainsKey(ChronicleModule.Name));
    }

    private static ChronicleEntry Entry(string id, int day, string kind, params string[] subjects)
    {
        return new ChronicleEntry(id, day, 1, kind, 3, subjects.Select(s => new ChronicleSubject(s, s)).ToList(), "TEXT.", [1]);
    }
}

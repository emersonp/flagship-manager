using Sim.Core;
using Sim.Core.Content;
using Sim.Storylets;

namespace Sim.Tests;

// The editor's preview entry point (1e part six, Task 5, design 4.7): a definition cast on any IStateSource, here a ship written by
// hand against the fixture pack, with a stream for each sample, and why when no one fits. Review focus 3: it casts as the module does.
public class StoryletPreviewTests
{
    private const string Id = "STORYLET_PREVIEW";

    // trait_b on three crew; CREW_01 is unfit, CREW_08 is dead. One death in the chronicle, 10 days ago. STORYLET_01 arrived two
    // cycles ago.
    private static readonly TestShipDef Ship = new(
        "preview",
        "Preview",
        500,
        10,
        [
            new TestShipCrew("CREW_01", null, null, null, false, null, null),
            new TestShipCrew("CREW_08", null, null, null, null, null, false),
        ],
        [
            new TestShipTrait("trait_b", "CREW_02", null, 2, 3, 1),
            new TestShipTrait("trait_b", "CREW_04", null, 2, 3, 1),
            new TestShipTrait("trait_b", "CREW_06", null, 2, 3, 1),
        ],
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(),
        [new TestShipEntry("death", 10, ["CREW_08"])],
        [new TestShipArrival("STORYLET_01", 14, 2)]);

    private static StoryletOption Plain(string id, string? requires = null)
    {
        return new StoryletOption(id, id, [], requires is null ? null : new Expr(requires));
    }

    private static readonly StoryletOption Checked = new("checked", "Checked", [], Check: new StoryletCheck("SKILL_A", 3, new StoryletOutcome([]), new StoryletOutcome([])));

    private static ContentPack PackWith(StoryletDef storylet)
    {
        return TestContent.Pack with { Storylets = [.. TestContent.Pack.Storylets, storylet] };
    }

    private static StoryletDef Storylet(string subject, string? requires = null, Cooldown? cooldown = null, string id = Id)
    {
        return new StoryletDef(id, 1, [StoryletsModuleTests.Slot("subject", subject)], requires is null ? null : new Expr(requires), [Plain("talk"), Plain("never", "subject.morale > 100"), Checked])
        {
            Cooldown = cooldown ?? Cooldown.None,
        };
    }

    private static (PreviewCast Preview, TestShipSource Source) Cast(ContentPack pack, int sample = 0, bool anyway = false, string id = Id)
    {
        var source = new TestShipSource(pack, Ship);
        return (Preview.Storylet(pack, id, source, source.Roster, sample, anyway), source);
    }

    [Fact]
    public void A_storylet_that_fits_casts_as_Casting_Cast_does_on_the_same_stream()
    {
        ContentPack pack = PackWith(Storylet("subject.trait.trait_b >= 1"));
        (PreviewCast preview, TestShipSource source) = Cast(pack);
        CastResult cast = new Casting(pack).Cast(Id, source, source.Roster, () => Preview.Stream(Id, 0), pack.CastTries, _ => false);

        Assert.Equal(CastOutcome.Cast, cast.Outcome);
        Assert.Equal(cast.Cast, preview.Cast);
        Assert.True(preview.RequiresHolds);
        Assert.Null(preview.RequiresFailed);
        Assert.Empty(preview.Why);
        Assert.False(preview.Unconditioned);
    }

    [Fact]
    public void Samples_0_1_and_2_give_three_casts_when_three_crew_fit_and_then_go_round()
    {
        ContentPack pack = PackWith(Storylet("subject.trait.trait_b >= 1"));

        List<string> subjects = Enumerable.Range(0, 6).Select(n => Cast(pack, n).Preview.Cast!["subject"]).ToList();

        Assert.Equal(["CREW_02", "CREW_04", "CREW_06"], subjects.Take(3).Order());
        Assert.Equal(subjects.Take(3), subjects.Skip(3));
    }

    [Fact]
    public void A_failing_slot_names_its_clause()
    {
        ContentPack pack = PackWith(Storylet("subject.fit && subject.trait.trait_b >= 3"));

        PreviewCast preview = Cast(pack).Preview;

        Assert.Null(preview.Cast);
        SlotWhy why = Assert.Single(preview.Why);
        Assert.Equal(new SlotWhy("subject", "subject.trait.trait_b >= 3", $"no one aboard has trait.trait_b >= 3, though the clauses before it held for {Cast(pack).Source.Roster.Count - 1}"), why);
        Assert.Empty(preview.OptionOffered);
        Assert.Null(preview.Handler);
    }

    // Widened in place (Step 2): every slot that fails on its own is named, and Failed, which Dev Play reads, still gives the first.
    [Fact]
    public void Every_slot_that_fails_on_its_own_is_named()
    {
        var storylet = new StoryletDef(Id, 1, [StoryletsModuleTests.Slot("subject", "subject.trait.trait_b >= 3"), StoryletsModuleTests.Slot("target", "target.morale > 100")], null, [Plain("talk")]);
        ContentPack pack = PackWith(storylet);
        (PreviewCast preview, TestShipSource source) = Cast(pack);

        Assert.Equal(["subject", "target"], preview.Why.Select(w => w.Slot));
        Assert.Equal("no one aboard has morale > 100", preview.Why[1].Reason);
        Assert.Equal(new FailedClause("subject", "subject.trait.trait_b >= 3", null), new Casting(pack).Failed(Id, source, source.Roster));
    }

    [Fact]
    public void A_failing_requires_names_its_clause_and_what_it_read()
    {
        ContentPack pack = PackWith(new StoryletDef(Id, 1, [], new Expr("ship.trait.trait_a == 0 && chronicle.count('death', 120) >= 2"), [Plain("talk")]));

        PreviewCast preview = Cast(pack).Preview;

        Assert.False(preview.RequiresHolds);
        Assert.Equal("chronicle.count('death', 120) >= 2 is 1", preview.RequiresFailed);
        Assert.Null(preview.Cast);
        Assert.Empty(preview.Why);
    }

    [Fact]
    public void A_requires_that_reads_the_cast_says_what_it_read_on_the_first_cast()
    {
        ContentPack pack = PackWith(Storylet("subject.trait.trait_b >= 1", "subject.loyalty > 100"));

        PreviewCast preview = Cast(pack).Preview;

        Assert.False(preview.RequiresHolds);
        Assert.Equal($"subject.loyalty > 100 is {TestContent.Pack.CrewTuning.StartingLoyalty}", preview.RequiresFailed);
    }

    [Fact]
    public void Out_of_tries_when_every_slot_has_someone_but_the_search_stops()
    {
        ContentPack pack = PackWith(new StoryletDef(Id, 1, [StoryletsModuleTests.Slot("subject", "subject.fit"), StoryletsModuleTests.Slot("target", "target.fit")],
            new Expr("subject.trait.trait_b > target.trait.trait_b"), [Plain("talk")])) with { CastTries = 2 };

        PreviewCast preview = Cast(pack).Preview;

        Assert.Null(preview.Cast);
        Assert.Equal([new SlotWhy("", "", Preview.OutOfTries)], preview.Why);
        Assert.True(preview.RequiresHolds);
    }

    // The ship's arrival says only when it last arrived, so a cast cooldown that holds is reported for every cast; the global
    // cooldown reads the same arrival. Both use the module's test: fewer cycles ago than the cooldown.
    [Theory]
    [InlineData(3, 0, false, true)]
    [InlineData(2, 0, true, true)]
    [InlineData(0, 3, true, false)]
    [InlineData(0, 2, true, true)]
    public void A_cooldown_that_holds_is_reported(int cast, int global, bool castClear, bool globalClear)
    {
        StoryletDef cooling = Storylet("subject.trait.trait_b >= 1", cooldown: new Cooldown(cast, global), id: "STORYLET_01");
        ContentPack pack = TestContent.Pack with { Storylets = TestContent.Pack.Storylets.Select(s => s.Id == cooling.Id ? cooling : s).ToList() };

        PreviewCast preview = Cast(pack, id: "STORYLET_01").Preview;

        Assert.Equal((castClear, globalClear), (preview.CastCooldownClear, preview.GlobalCooldownClear));
        Assert.NotNull(preview.Cast);
    }

    [Fact]
    public void Anyway_fills_the_slots_in_roster_order_and_says_so()
    {
        var storylet = new StoryletDef(Id, 1, [StoryletsModuleTests.Slot("subject", "subject.morale > 100"), StoryletsModuleTests.Slot("target", "target.morale > 100")], null, [Plain("talk")]);
        (PreviewCast preview, TestShipSource source) = Cast(PackWith(storylet), anyway: true);

        Assert.True(preview.Unconditioned);
        Assert.Equal(new Dictionary<string, string> { ["subject"] = source.Roster[0], ["target"] = source.Roster[1] }, preview.Cast);
        Assert.Equal(["subject", "target"], preview.Why.Select(w => w.Slot));
        Assert.Equal(new Dictionary<string, bool> { ["talk"] = true }, preview.OptionOffered);
    }

    [Fact]
    public void Anyway_leaves_a_cast_that_fits_as_it_is()
    {
        ContentPack pack = PackWith(Storylet("subject.trait.trait_b >= 1"));

        Assert.Equal(Cast(pack).Preview, Cast(pack, anyway: true).Preview, new PreviewComparer());
        Assert.False(Cast(pack, anyway: true).Preview.Unconditioned);
    }

    [Fact]
    public void An_option_whose_requires_fails_is_not_offered()
    {
        PreviewCast preview = Cast(PackWith(Storylet("subject.trait.trait_b >= 1"))).Preview;

        Assert.Equal(new Dictionary<string, bool> { ["talk"] = true, ["never"] = false, ["checked"] = true }, preview.OptionOffered);
    }

    // The handler is one fit to send and not in the cast (CREW_01 is unfit, CREW_08 dead), and rotates with the sample.
    [Fact]
    public void A_check_options_handler_rotates_among_those_fit_to_send()
    {
        ContentPack pack = PackWith(Storylet("subject.trait.trait_b >= 1"));
        foreach (int n in Enumerable.Range(0, 10))
        {
            (PreviewCast preview, TestShipSource source) = Cast(pack, n);
            List<string> fit = source.Roster.Where(id => id != "CREW_01" && id != preview.Cast!["subject"]).ToList();

            Assert.Equal(fit[n % fit.Count], preview.Handler);
        }
    }

    [Fact]
    public void A_vignette_previews_the_same_way()
    {
        var vignette = new VignetteDef("VIGNETTE_PREVIEW", [StoryletsModuleTests.Slot("subject", "subject.trait.trait_b >= 1")], null, [], [Plain("nod")], "nod");
        ContentPack pack = TestContent.Pack with { Vignettes = [.. TestContent.Pack.Vignettes, vignette] };
        var source = new TestShipSource(pack, Ship);

        PreviewCast preview = Preview.Vignette(pack, "VIGNETTE_PREVIEW", source, source.Roster, 0, anyway: false);

        Assert.Contains(preview.Cast!["subject"], new[] { "CREW_02", "CREW_04", "CREW_06" });
        Assert.Equal(new Dictionary<string, bool> { ["nod"] = true }, preview.OptionOffered);
        Assert.Null(preview.Handler);
    }

    private sealed class PreviewComparer : IEqualityComparer<PreviewCast>
    {
        public bool Equals(PreviewCast? x, PreviewCast? y)
        {
            return x is not null && y is not null && x.Cast!.SequenceEqual(y.Cast!) && x.Unconditioned == y.Unconditioned && x.Handler == y.Handler;
        }

        public int GetHashCode(PreviewCast obj)
        {
            return 0;
        }
    }
}

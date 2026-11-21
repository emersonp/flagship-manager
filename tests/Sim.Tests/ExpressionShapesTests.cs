using Sim.Content;
using Sim.Core;
using Sim.Core.Content;
using Sim.Expressions;

namespace Sim.Tests;

// Expressions as rows (1e part six, design 4.5): the ten shapes read from the parsed tree, written back in the house spelling, and
// every expression in both packs a row set that renders back to itself.
public class ExpressionShapesTests : IClassFixture<RepoContentGuard>
{
    private static readonly string[] Pair = ["subject", "target"];

    private static readonly ExpressionBook Book = new(
        [
            TraitDef.Plain("shaken") with { On = TraitPlaces.Crew },
            TraitDef.Plain("avoids") with { On = TraitPlaces.Pair },
            TraitDef.Plain("hull_strain") with { On = TraitPlaces.Ship },
            TraitDef.Plain("came_back") with { On = TraitPlaces.Crew, Levels = new Dictionary<int, string> { [1] = "settled", [2] = "withdrawn" } },
            TraitDef.Plain("sensors_quirk") with { On = TraitPlaces.Ship, Levels = new Dictionary<int, string> { [1] = "noticed", [2] = "let_ride" } },
        ],
        ["SKILL_A", "SKILL_B"],
        ["sent", "hurt", "rested", "lost_crewmate", "lost_mission"],
        20,
        ["STORYLET_A"],
        ["VIGNETTE_A"]);

    public static TheoryData<Row, string> EachShape => new()
    {
        { Row(ShapeKind.Presence, "subject", "aboard"), "subject.aboard" },
        { Row(ShapeKind.Presence, "target", "fit") with { Negated = true }, "!target.fit" },
        { Row(ShapeKind.Trait, "subject", "shaken", ">=", "1"), "subject.trait.shaken >= 1" },
        { Row(ShapeKind.Trait, "subject", "came_back", "==", "withdrawn"), "subject.trait.came_back == 'withdrawn'" },
        { Row(ShapeKind.Trait, "subject", "came_back", "==", "0"), "subject.trait.came_back == 0" },
        { Row(ShapeKind.Trait, "ship", "sensors_quirk", "!=", "let_ride"), "ship.trait.sensors_quirk != 'let_ride'" },
        { Row(ShapeKind.Trait, "ship", "hull_strain", "<", "2"), "ship.trait.hull_strain < 2" },
        { Row(ShapeKind.PairTrait, "subject", "avoids", "<=", "4") with { Toward = "target" }, "subject.trait.avoids.toward(target) <= 4" },
        { Row(ShapeKind.Stat, "subject", "morale", ">", "25"), "subject.morale > 25" },
        { Row(ShapeKind.Stat, "target", "loyalty", "<=", "-1"), "target.loyalty <= -1" },
        { Row(ShapeKind.Skill, "target", "SKILL_B", ">=", "3"), "target.skills.SKILL_B >= 3" },
        { Row(ShapeKind.HistoryLast, "subject", "hurt"), "subject.history.last('hurt')" },
        { Row(ShapeKind.HistoryLast, "target", "sent") with { Negated = true }, "!target.history.last('sent')" },
        { Row(ShapeKind.HistoryCount, "subject", "lost_mission", ">", "0") with { Window = 2 }, "subject.history.count('lost_mission', 2) > 0" },
        { Row(ShapeKind.ChronicleCount, "ship", "death", ">=", "2") with { Window = 120 }, "chronicle.count('death', 120) >= 2" },
        { Row(ShapeKind.ChronicleCount, "target", "call", "==", "1") with { Window = 60 }, "target.chronicle.count('call', 60) == 1" },
        { Row(ShapeKind.StoryletSince, "ship", "STORYLET_A", ">=", "40") with { Unit = "days" }, "storylet.STORYLET_A.daysSince >= 40" },
        { Row(ShapeKind.StoryletSince, "ship", "STORYLET_A", "<=", "3") with { Unit = "cycles" }, "storylet.STORYLET_A.cyclesSince <= 3" },
        { Row(ShapeKind.TraitSince, "subject", "shaken", ">=", "2") with { Unit = "cycles" }, "subject.trait.shaken.cyclesSince >= 2" },
        { Row(ShapeKind.TraitSince, "ship", "hull_strain", "<", "30") with { Unit = "days" }, "ship.trait.hull_strain.daysSince < 30" },
    };

    [Theory]
    [MemberData(nameof(EachShape))]
    public void A_row_writes_its_house_spelling_and_reads_back_as_itself(Row row, string text)
    {
        string written = Shapes.Write([row]);

        Assert.Equal(text, written);
        Assert.NotNull(Book.Check(written, Pair, out ExpressionError? error));
        Assert.Null(error);
        RowsRead read = Shapes.Read(written, Pair, Book);
        Assert.Null(read.Reason);
        Assert.Equal([row], read.Rows!);
    }

    [Fact]
    public void Clauses_joined_by_and_are_rows_in_order_and_a_range_stays_two()
    {
        const string text = "subject.aboard && subject.trait.avoids.toward(target) >= 3 && subject.trait.avoids.toward(target) <= 4";

        RowsRead read = Shapes.Read(text, Pair, Book);

        Assert.Equal([ShapeKind.Presence, ShapeKind.PairTrait, ShapeKind.PairTrait], read.Rows!.Select(r => r.Kind));
        Assert.Equal(text, Shapes.Write(read.Rows!));
    }

    [Fact]
    public void Rows_are_read_from_the_tree_so_spacing_quotes_and_grouping_do_not_matter()
    {
        RowsRead read = Shapes.Read("(subject.morale>=26) && (subject.trait.came_back == \"settled\" && target.fit)", Pair, Book);

        Assert.Equal("subject.morale >= 26 && subject.trait.came_back == 'settled' && target.fit", Shapes.Write(read.Rows!));
    }

    [Theory]
    [InlineData("subject.fit || subject.aboard", Shapes.UsesOr)]
    [InlineData("subject.morale + 1 > 2", Shapes.UsesArithmetic)]
    [InlineData("-subject.morale < 0", Shapes.UsesArithmetic)]
    [InlineData("!(subject.fit && subject.aboard)", Shapes.GroupsNot)]
    [InlineData("!(subject.morale > 3)", Shapes.GroupsNot)]
    [InlineData("subject.morale > target.morale", Shapes.ComparesReadings)]
    [InlineData("subject != target", Shapes.ComparesReadings)]
    [InlineData("subject.aboard && 1 < subject.morale", Shapes.ValueFirst)]
    [InlineData("'withdrawn' == subject.trait.came_back", Shapes.ValueFirst)]
    [InlineData("cycle.number > 12", Shapes.NoShape)]
    [InlineData("subject.fit == true", Shapes.NoShape)]
    // Only the first reason, in reading order.
    [InlineData("subject.morale * 2 > 1 || subject.fit", Shapes.UsesArithmetic)]
    [InlineData("subject.fit && (subject.aboard || target.fit) && subject.morale + 1 > 2", Shapes.UsesOr)]
    public void A_valid_expression_that_is_not_rows_stays_text_with_one_reason(string text, string reason)
    {
        Assert.NotNull(Book.Check(text, Pair, out _));

        RowsRead read = Shapes.Read(text, Pair, Book);

        Assert.Null(read.Rows);
        Assert.Equal(reason, read.Reason);
    }

    [Fact]
    public void A_refused_expression_is_never_rows_and_gives_the_rewrites_error()
    {
        Assert.Null(Book.Check("subject.fit && captain.fit", Pair, out ExpressionError? error));

        RowsRead read = Shapes.Read("subject.fit && captain.fit", Pair, Book);

        Assert.Null(read.Rows);
        Assert.Equal(error!.Message, read.Reason);
        Assert.StartsWith("captain is not a slot this expression can read", read.Reason);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("tests/fixtures/content")]
    public void Every_expression_in_the_pack_is_rows_that_write_back_to_it_exactly(string root)
    {
        ContentLoadResult result = ContentLoader.LoadDirectory(Path.Combine(TestContent.Root, root));
        ContentPack pack = result.Pack ?? throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Message)));
        var book = new ExpressionBook(pack.Traits, pack.Skills.Select(s => s.Id), HistoryKinds.All, pack.HistoryCycles, pack.Storylets.Select(s => s.Id), pack.Vignettes.Select(v => v.Id));

        var expressions = new List<(string Where, string Source, IReadOnlyList<string> Slots)>();
        void Add(string id, IReadOnlyList<CastSlot> cast, Expr? requires, IEnumerable<StoryletOption> options)
        {
            List<string> slots = cast.Select(c => c.Name).ToList();
            for (int k = 0; k < cast.Count; k++)
            {
                expressions.Add(($"{id} cast.{cast[k].Name}", cast[k].Condition.Source, slots[..(k + 1)]));
            }

            if (requires is not null)
            {
                expressions.Add(($"{id} requires", requires.Source, slots));
            }

            foreach (StoryletOption option in options.Where(o => o.Requires is not null))
            {
                expressions.Add(($"{id} options.{option.Id}.requires", option.Requires!.Source, slots));
            }
        }

        foreach (StoryletDef storylet in pack.Storylets)
        {
            Add(storylet.Id, storylet.Cast, storylet.Requires, storylet.Options);
        }

        foreach (VignetteDef vignette in pack.Vignettes)
        {
            Add(vignette.Id, vignette.Cast, vignette.Requires, vignette.Options);
        }

        Assert.NotEmpty(expressions);
        Assert.Contains(expressions, e => e.Where.Contains("options.", StringComparison.Ordinal));
        foreach ((string where, string source, IReadOnlyList<string> slots) in expressions)
        {
            RowsRead read = Shapes.Read(source, slots, book);
            Assert.True(read.Rows is not null, $"{where}: \"{source}\" stays text: {read.Reason}");
            Assert.Equal(source, Shapes.Write(read.Rows!));
        }
    }

    [Fact]
    public void Write_refuses_a_row_missing_a_part_it_needs()
    {
        Assert.Throws<ArgumentException>(() => Shapes.Write([Row(ShapeKind.Stat, "subject", "morale")]));
        Assert.Throws<ArgumentException>(() => Shapes.Write([Row(ShapeKind.HistoryCount, "subject", "hurt", ">", "0")]));
        Assert.Throws<ArgumentException>(() => Shapes.Write([Row(ShapeKind.Stat, "subject", "morale", ">", "1") with { Negated = true }]));
    }

    private static Row Row(ShapeKind kind, string who, string name, string? op = null, string? value = null)
    {
        return new Row(kind, who, name, null, op, value, null, null, false);
    }
}

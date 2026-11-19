using System.Diagnostics;
using Sim.Core.Content;
using Sim.Expressions;

namespace Sim.Tests;

// The expression language (1e part four, design 4.3 and section 9): every read of the state view, every refusal with its
// position, and nothing that reaches past the view.
public class ExpressionsTests
{
    private const string A = "CREW_A";
    private const string B = "CREW_B";

    private static readonly string[] Pair = ["subject", "target"];

    private static readonly ExpressionBook Book = new(
        [
            TraitDef.Plain("shaken") with { On = TraitPlaces.Crew },
            TraitDef.Plain("avoids") with { On = TraitPlaces.Pair },
            TraitDef.Plain("hull_strain") with { On = TraitPlaces.Ship },
            TraitDef.Plain("came_back") with { On = TraitPlaces.Crew, Levels = new Dictionary<int, string> { [1] = "settled", [2] = "withdrawn" } },
            TraitDef.Plain("mood"),
        ],
        ["SKILL_A", "SKILL_B"],
        ["sent", "hurt", "rested", "lost_crewmate", "lost_mission"],
        20);

    private static readonly Dictionary<string, string> Cast = new() { ["subject"] = A, ["target"] = B };

    [Theory]
    [InlineData("subject.morale == 40", true)]
    [InlineData("subject.morale > 40", false)]
    [InlineData("subject.loyalty >= 70", true)]
    [InlineData("subject.loyalty < 70", false)]
    [InlineData("subject.skills.SKILL_A == 5", true)]
    [InlineData("subject.skills.SKILL_B > 0", false)]
    [InlineData("subject.fit", true)]
    [InlineData("target.fit", false)]
    [InlineData("subject.aboard", true)]
    [InlineData("!subject.aboard", false)]
    [InlineData("subject.trait.shaken == 2", true)]
    [InlineData("subject.trait.shaken > 2", false)]
    [InlineData("subject.trait.avoids.toward(target) == 1", true)]
    [InlineData("target.trait.avoids.toward(subject) > 0", false)]
    [InlineData("subject.trait.came_back == 'withdrawn'", true)]
    [InlineData("subject.trait.came_back == 'settled'", false)]
    [InlineData("'withdrawn' == subject.trait.came_back", true)]
    [InlineData("subject.trait.came_back != \"settled\"", true)]
    [InlineData("subject.history.count('hurt', 5) >= 2", true)]
    [InlineData("subject.history.count('hurt', 1) > 0", false)]
    [InlineData("subject.history.last('sent')", true)]
    [InlineData("target.history.last('sent')", false)]
    [InlineData("subject != target", true)]
    [InlineData("subject == target", false)]
    [InlineData("ship.trait.hull_strain == 1", true)]
    [InlineData("ship.trait.hull_strain > 1", false)]
    [InlineData("cycle.number == 12", true)]
    [InlineData("cycle.number > 12", false)]
    [InlineData("subject.trait.mood == 0 && ship.trait.mood == 0", true)]
    [InlineData("subject.morale + subject.loyalty * 2 - 10 == 170 && -subject.morale < 0", true)]
    public void Each_read_of_the_view_holds_or_not(string source, bool expected)
    {
        CheckedExpression expression = Checked(source, Pair);

        Assert.Equal(expected, Book.Evaluate(expression, new FakeState(), Cast));
    }

    [Theory]
    [InlineData("subject.trait.nosuch > 0", "No trait named nosuch", 14)]
    [InlineData("subject.rel(target) > 0", "rel() waits for relationships (Phase 4)", 8)]
    [InlineData("rel(subject, target) > 0", "rel() waits for relationships (Phase 4)", 0)]
    [InlineData("subject.grade.ATTRIBUTE_A > 0", "grade waits for 1h", 8)]
    [InlineData("subject.skills.SKILL_Z > 0", "No skill named SKILL_Z", 15)]
    [InlineData("subject.history.count('nosuch', 3) > 0", "No history kind named nosuch", 22)]
    [InlineData("subject.history.count('hurt', 99) > 0", "1 to 20", 30)]
    [InlineData("subject.history.count('hurt', 0) > 0", "1 to 20", 30)]
    [InlineData("subject.history.count('hurt', subject.morale) > 0", "history.count('hurt', 5)", 30)]
    [InlineData("subject.history.last(subject) > 0", "history.last('hurt')", 21)]
    [InlineData("subject.trait.shaken.toward(target) > 0", "shaken is not on a pair", 21)]
    [InlineData("subject.trait.avoids > 0", "avoids is held toward someone", 14)]
    [InlineData("subject.trait.avoids.toward(cycle) > 0", "toward takes a slot", 28)]
    [InlineData("ship.trait.shaken > 0", "shaken is on crew, not the ship", 11)]
    [InlineData("ship.trait.avoids.toward(subject) > 0", "avoids is on a pair, not the ship", 11)]
    [InlineData("subject.trait.hull_strain > 0", "hull_strain is on the ship", 14)]
    [InlineData("subject.trait.came_back == 'gone'", "came_back has no level named gone; its levels are settled, withdrawn", 27)]
    [InlineData("subject.trait.shaken == 'gone'", "shaken has no named levels", 24)]
    [InlineData("subject.morale == 'gone'", "A quoted name is", 18)]
    [InlineData("subject.morale", "true or false", 0)]
    [InlineData("subject.morale / 2 > 1", "'/' is not part of the expression language", 15)]
    [InlineData("subject.fit and subject.aboard", "and is written &&", 12)]
    [InlineData("subject.fit or subject.aboard", "or is written ||", 12)]
    [InlineData("not subject.fit", "not is written !", 0)]
    [InlineData("subject.fit &&& subject.aboard", "'&' is not part of the expression language", 14)]
    [InlineData("subject.morale = 5", "'=' is not part of the expression language", 15)]
    [InlineData("", "An expression is empty", 0)]
    [InlineData("subject.fit && 'open", "never closed", 15)]
    [InlineData("subject.fit && 'two words'", "A quoted name holds only", 15)]
    [InlineData("subject.Id == subject.Id", "A crew member has nothing named Id", 8)]
    [InlineData("cycle.morale > 0", "cycle reads only number", 6)]
    [InlineData("ship.morale > 0", "ship reads only trait", 5)]
    [InlineData("subject.morale.value > 0", "Nothing is read from subject.morale", 14)]
    [InlineData("subject.trait > 0", "A trait is read as subject.trait.some_trait", 8)]
    [InlineData("subject.fit && captain.fit", "captain is not a slot this expression can read; it reads subject, target, ship, and cycle", 15)]
    public void A_refusal_names_its_position(string source, string message, int position)
    {
        Assert.Null(Book.Check(source, Pair, out ExpressionError? error));

        Assert.NotNull(error);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Equal(position, error.Position);
    }

    // The parser's own errors come back at their place in the source, though the rewrite has changed the text before them.
    [Fact]
    public void A_parser_error_after_a_rewritten_part_keeps_its_position()
    {
        const string source = "subject.trait.came_back == 'withdrawn' && subject.trait.shaken > 0 && (subject.fit";

        Assert.Null(Book.Check(source, Pair, out ExpressionError? error));

        Assert.NotNull(error);
        Assert.Equal(source.Length, error.Position);
        Assert.DoesNotContain("at index", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_later_slot_cannot_be_read()
    {
        Assert.Null(Book.Check("subject.fit && target.fit", ["subject"], out ExpressionError? error));
        Assert.Equal(new ExpressionError("target is not a slot this expression can read; it reads subject, ship, and cycle.", 15), error);

        Assert.Null(Book.Check("subject.trait.avoids.toward(target) > 0", ["subject"], out error));
        Assert.Equal(28, error!.Position);
    }

    [Fact]
    public void Clauses_count_the_top_level_ands()
    {
        Assert.Equal(3, Checked("subject.fit && (subject.morale < 30 || subject.loyalty < 30) && !subject.history.last('sent')", Pair).Clauses);
        Assert.Equal(1, Checked("subject.fit", Pair).Clauses);
        Assert.Equal(1, Checked("subject.fit || subject.aboard", Pair).Clauses);
        Assert.Equal(1, Checked("subject.fit && subject.aboard || cycle.number > 1", Pair).Clauses);
        Assert.Equal(3, Checked("(subject.fit && subject.aboard) && cycle.number > 1", Pair).Clauses);
    }

    // Dev Play's first failing clause (Task 7): the same clauses as the count, each its own checked expression over the same slots,
    // read inside any parentheses that wrap it whole.
    [Theory]
    [InlineData("subject.fit && (subject.morale < 30 || subject.loyalty < 30) && !subject.history.last('sent')", "subject.fit ; subject.morale < 30 || subject.loyalty < 30 ; !subject.history.last('sent')")]
    [InlineData("((subject.fit && subject.aboard) && target.trait.avoids.toward(subject) >= 1)", "subject.fit ; subject.aboard ; target.trait.avoids.toward(subject) >= 1")]
    [InlineData("subject.fit && subject.aboard || cycle.number > 1", "subject.fit && subject.aboard || cycle.number > 1")]
    [InlineData("(subject.fit || subject.aboard) && (cycle.number > 1)", "subject.fit || subject.aboard ; cycle.number > 1")]
    public void Clauses_split_as_they_are_counted(string source, string expected)
    {
        CheckedExpression expression = Checked(source, Pair);

        IReadOnlyList<CheckedExpression> clauses = Book.Split(expression);

        Assert.Equal(expected, string.Join(" ; ", clauses.Select(c => c.Source)));
        Assert.Equal(expression.Clauses, clauses.Count);
        Assert.All(clauses, c => Assert.Equal(Pair, c.Slots));
    }

    [Fact]
    public void Traits_read_lists_each_trait_with_its_slot()
    {
        CheckedExpression expression = Checked(
            "subject.trait.shaken > 0 && subject.trait.avoids.toward(target) > 0 && ship.trait.hull_strain == 0 && subject.trait.shaken < 3 && target.trait.shaken == 0",
            Pair);

        Assert.Equal(
            [new TraitRead("shaken", "subject", null), new TraitRead("avoids", "subject", "target"), new TraitRead("hull_strain", "ship", null), new TraitRead("shaken", "target", null)],
            expression.Traits);
    }

    [Fact]
    public void A_ship_expression_reads_no_slot()
    {
        CheckedExpression expression = Checked("ship.trait.hull_strain > 0 && cycle.number > 10", []);

        Assert.True(Book.Evaluate(expression, new FakeState(), new Dictionary<string, string>()));
        Assert.Null(Book.Check("subject.fit", [], out ExpressionError? error));
        Assert.Equal("subject is not a slot this expression can read; it reads ship and cycle.", error!.Message);
    }

    [Fact]
    public void Three_slots_are_read_in_order()
    {
        CheckedExpression expression = Checked("first != third && third.morale == 10", ["first", "second", "third"]);

        Assert.True(Book.Evaluate(expression, new FakeState(), new Dictionary<string, string> { ["first"] = A, ["second"] = "CREW_C", ["third"] = B }));
        Assert.Throws<ArgumentException>(() => Book.Check("true", ["a", "b", "c", "d"], out _));
    }

    // Focus 1: nothing reaches past the state view. Each is refused when it is checked, never when it is played.
    [Theory]
    [InlineData("subject.GetType() != null")]
    [InlineData("subject.ToString() == \"x\"")]
    [InlineData("subject.Equals(target)")]
    [InlineData("subject.GetHashCode() > 0")]
    [InlineData("\"abc\".Length > 0")]
    [InlineData("System.IO.File.Exists(\"x\")")]
    [InlineData("typeof(string) != null")]
    [InlineData("new object() != null")]
    [InlineData("Math.Max(1, 2) > 0")]
    [InlineData("Convert.ToInt32(\"1\") > 0")]
    [InlineData("DateTime.Now.Year > 0")]
    [InlineData("(object)subject != null")]
    [InlineData("subject is object")]
    [InlineData("default(int) == 0")]
    [InlineData("subject.morale = 5")]
    [InlineData("x => true")]
    [InlineData("subject.morale++ > 0")]
    [InlineData("subject.trait[\"shaken\"] > 0")]
    [InlineData("subject.skills[\"SKILL_A\"] > 0")]
    [InlineData("subject.trait.toward(\"avoids\", target) > 0")]
    [InlineData("1 / 0 > 0")]
    [InlineData("5 % 2 == 1")]
    [InlineData("subject.history.count(\"hurt\", 5).ToString() == \"1\"")]
    [InlineData("subject.morale.GetType() != null")]
    [InlineData("(subject).GetType() != null")]
    [InlineData("(subject).trait.toward(\"avoids\", target) > 0")]
    [InlineData("subject.fit ? true : false")]
    [InlineData("@subject.fit")]
    [InlineData("subject.fit; true")]
    [InlineData("_1.fit")]
    [InlineData("subject.fit && null == null")]
    public void Nothing_reaches_past_the_view(string source)
    {
        Assert.Null(Book.Check(source, Pair, out ExpressionError? error));
        Assert.NotNull(error);
    }

    // Behind the rewrite, the whitelist refuses on its own what DynamicExpresso's reflection guard lets through (section 9, Task 1).
    [Theory]
    [InlineData("subject.GetType().Name == \"x\"")]
    [InlineData("subject.ToString() == \"x\"")]
    [InlineData("subject.Equals(target)")]
    [InlineData("subject.GetHashCode() > 0")]
    [InlineData("\"abc\".Length > 0")]
    [InlineData("1 / 0 > 0")]
    [InlineData("5 % 2 == 1")]
    [InlineData("subject.trait[\"shaken\"].ToString() == \"1\"")]
    [InlineData("1.5 > 1")]
    public void The_whitelist_refuses_on_its_own(string rewritten)
    {
        Assert.NotNull(Book.ParseError(rewritten, Pair));
    }

    [Fact]
    public void The_whitelist_lets_the_rewritten_language_through()
    {
        Assert.Null(Book.ParseError("subject.trait[\"shaken\"] > 0 && subject.trait.toward(\"avoids\", target) == 1 && subject.skills[\"SKILL_A\"] > 0 && subject.history.count(\"hurt\", 5) > 0 && subject != target", Pair));
    }

    [Fact]
    public void Ten_thousand_evaluations_take_well_under_a_second()
    {
        CheckedExpression expression = Checked(
            "subject.fit && subject.trait.shaken > 0 && subject.history.count('hurt', 5) >= 2 && subject.trait.avoids.toward(target) > 0 && target != subject",
            Pair);
        var state = new FakeState();
        Book.Evaluate(expression, state, Cast);

        var clock = Stopwatch.StartNew();
        int held = 0;
        for (int i = 0; i < 10_000; i++)
        {
            held += Book.Evaluate(expression, state, Cast) ? 1 : 0;
        }

        clock.Stop();
        Assert.Equal(10_000, held);
        Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(250), $"10,000 evaluations took {clock.Elapsed.TotalMilliseconds} ms.");
    }

    [Fact]
    public void A_cast_missing_a_slot_is_refused()
    {
        CheckedExpression expression = Checked("target.fit", Pair);

        Assert.Throws<ArgumentException>(() => Book.Evaluate(expression, new FakeState(), new Dictionary<string, string> { ["subject"] = A }));
    }

    private static CheckedExpression Checked(string source, IReadOnlyList<string> slots)
    {
        CheckedExpression? expression = Book.Check(source, slots, out ExpressionError? error);
        Assert.True(expression is not null, error?.ToString());
        return expression;
    }

    // A holds shaken 2, came_back withdrawn, avoids 1 toward B; was hurt two and three cycles ago and sent last cycle. B is unfit.
    private sealed class FakeState : IStateSource
    {
        private readonly Dictionary<(string Trait, string? Crew, string? Target), int> traits = new()
        {
            [("shaken", A, null)] = 2,
            [("came_back", A, null)] = 2,
            [("avoids", A, B)] = 1,
            [("hull_strain", null, null)] = 1,
        };

        private readonly List<(string Crew, string Kind, int Ago)> history = [(A, "hurt", 2), (A, "hurt", 3), (A, "sent", 1), (B, "rested", 1)];

        public int Cycle => 12;

        public int Morale(string crewId)
        {
            return crewId == A ? 40 : 10;
        }

        public int Loyalty(string crewId)
        {
            return crewId == A ? 70 : 50;
        }

        public int Skill(string crewId, string skill)
        {
            return crewId == A && skill == "SKILL_A" ? 5 : 0;
        }

        public bool IsFit(string crewId)
        {
            return crewId != B;
        }

        public bool IsAboard(string crewId)
        {
            return true;
        }

        public int Trait(string trait, string? crewId, string? targetId = null)
        {
            return traits.GetValueOrDefault((trait, crewId, targetId));
        }

        public int History(string crewId, string kind, int cycles)
        {
            return history.Count(h => h.Crew == crewId && h.Kind == kind && h.Ago <= cycles);
        }
    }
}

using System.Globalization;
using System.Linq.Expressions;

namespace Sim.Expressions;

// Dev Play's "the first clause that failed" (1e part four, design 4.10): an expression read one top-level && clause at a time.
public sealed partial class ExpressionBook
{
    // The expression's top-level && clauses in reading order, each checked on its own over the same slots, as any expression is:
    // the rewrite and the whitelist cover each. They are the clauses specificity counts. An expression of one clause is itself, and
    // so is one whose text does not split as its tree does, which a checked expression should never be.
    public IReadOnlyList<CheckedExpression> Split(CheckedExpression expression)
    {
        if (expression.Clauses == 1)
        {
            return [expression];
        }

        List<string> parts = Rewrite.TopLevelClauses(expression.Source);
        List<CheckedExpression> clauses = parts.Select(p => Check(p, expression.Slots, out _)).OfType<CheckedExpression>().ToList();
        return parts.Count == expression.Clauses && clauses.Count == parts.Count ? clauses : [expression];
    }

    // What a clause read on this cast, for the editor's "Requires fails: ... is 1" (1e part six, design 4.7): for a comparison of a
    // reading with a value, the reading ("1", "true"); for anything else, whether the clause held ("false").
    public string Reading(CheckedExpression clause, IStateSource state, IReadOnlyDictionary<string, string> cast)
    {
        Expression tree = clause.Lambda.Expression;
        if (tree is BinaryExpression { NodeType: ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThan or ExpressionType.LessThanOrEqual } comparison
            && (Reads(comparison.Left), Reads(comparison.Right)) is (true, false) or (false, true))
        {
            Expression side = Reads(comparison.Left) ? comparison.Left : comparison.Right;
            ParameterExpression[] parameters = [.. clause.Lambda.DeclaredParameters.Select(p => p.Expression)];
            var read = Expression.Lambda<Func<CrewView?, CrewView?, CrewView?, ShipView, CycleView, StoryletsView, ChronicleView, object?>>(
                Expression.Convert(Unwrapped(side), typeof(object)), parameters).Compile();
            IReadOnlyList<string> slots = clause.Slots;
            object? value = read(
                slots.Count > 0 ? View(state, cast, slots[0]) : null,
                slots.Count > 1 ? View(state, cast, slots[1]) : null,
                slots.Count > 2 ? View(state, cast, slots[2]) : null,
                new ShipView(state),
                new CycleView(state),
                new StoryletsView(state),
                new ChronicleView(state, null));
            return Shown(value);
        }

        return Shown(Evaluate(clause, state, cast));
    }

    private static string Shown(object? value)
    {
        return value switch
        {
            null => "unset",
            bool truth => truth ? "true" : "false",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "unset",
        };
    }

    // A widening the parser added to compare an int with a long or a nullable: the reading is what is under it.
    private static Expression Unwrapped(Expression side)
    {
        return side is UnaryExpression { NodeType: ExpressionType.Convert } convert ? Unwrapped(convert.Operand) : side;
    }

    // Whether a side reads the state (any parameter), rather than being a value written in the expression.
    private static bool Reads(Expression side)
    {
        var finder = new ParameterFinder();
        finder.Visit(side);
        return finder.Found;
    }

    private sealed class ParameterFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found = true;
            return node;
        }
    }
}

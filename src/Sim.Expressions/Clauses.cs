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
}

using DynamicExpresso;

namespace Sim.Expressions;

// Why an expression was refused, and where: Position is the 0-based index in the source as written.
public sealed record ExpressionError(string Message, int Position);

// One trait an expression reads: on a slot or "ship", and toward a slot for a pair trait. The lint reads these, and "where it came
// from" takes the first.
public sealed record TraitRead(string Trait, string Slot, string? Toward);

// An expression checked against the book: its slots in reading order, its top-level && clauses (specificity, design 4.5), and the
// traits it reads in reading order, each once. It is compiled the first time it is evaluated, and only then.
public sealed class CheckedExpression
{
    private readonly Lambda lambda;
    private Func<CrewView?, CrewView?, CrewView?, ShipView, CycleView, bool>? compiled;

    internal CheckedExpression(string source, IReadOnlyList<string> slots, int clauses, IReadOnlyList<TraitRead> traits, Lambda lambda)
    {
        Source = source;
        Slots = slots;
        Clauses = clauses;
        Traits = traits;
        this.lambda = lambda;
    }

    public string Source { get; }

    public IReadOnlyList<string> Slots { get; }

    public int Clauses { get; }

    public IReadOnlyList<TraitRead> Traits { get; }

    // The fixed signature: three slots (unbound ones under names no expression can write), then ship and cycle. A race compiles
    // twice and keeps either; both are the same.
    internal Func<CrewView?, CrewView?, CrewView?, ShipView, CycleView, bool> Compiled =>
        compiled ??= lambda.Compile<Func<CrewView?, CrewView?, CrewView?, ShipView, CycleView, bool>>();
}

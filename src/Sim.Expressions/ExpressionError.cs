using DynamicExpresso;
using Sim.Core.Content;

namespace Sim.Expressions;

// Why an expression was refused, and where: Position is the 0-based index in the source as written.
public sealed record ExpressionError(string Message, int Position);

// A history kind an expression reads with last(), on a slot, where no ! stands directly before the read: what a vignette's head says
// brought it ("lost a crewmate").
public sealed record LastRead(string Kind, string Slot);

// An expression checked against the book: its slots in reading order, its top-level && clauses (specificity, design 4.5), and the
// traits it reads in reading order, each once. It is compiled the first time it is evaluated, and only then.
public sealed class CheckedExpression
{
    private readonly Lambda lambda;
    private Func<CrewView?, CrewView?, CrewView?, ShipView, CycleView, StoryletsView, bool>? compiled;

    internal CheckedExpression(string source, IReadOnlyList<string> slots, int clauses, IReadOnlyList<TraitRead> traits, IReadOnlyList<LastRead> lasts, Lambda lambda)
    {
        Lasts = lasts;
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

    public IReadOnlyList<LastRead> Lasts { get; }

    // The parsed tree, for reading it with only the traits known (ExpressionBook.OnTraits).
    internal Lambda Lambda => lambda;

    // The fixed signature: three slots (unbound ones under names no expression can write), then ship and cycle. A race compiles
    // twice and keeps either; both are the same.
    internal Func<CrewView?, CrewView?, CrewView?, ShipView, CycleView, StoryletsView, bool> Compiled =>
        compiled ??= lambda.Compile<Func<CrewView?, CrewView?, CrewView?, ShipView, CycleView, StoryletsView, bool>>();
}

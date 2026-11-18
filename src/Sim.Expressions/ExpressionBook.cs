using System.Linq.Expressions;
using System.Text.RegularExpressions;
using DynamicExpresso;
using DynamicExpresso.Exceptions;
using Sim.Core.Content;

namespace Sim.Expressions;

// The expression language of storylets and vignettes (1e part four, design 4.3), built once per pack from what it may read: the
// trait definitions, the skill ids, the history kinds, and history.cycles. Check parses an expression against the state view's
// types and refuses anything else with its position (the loader checks with it); Evaluate runs a checked expression against the
// state (the storylets module runs with it), compiled the first time.
public sealed partial class ExpressionBook
{
    public const int MaxSlots = 3;

    public const string Ship = "ship";

    public const string Cycle = "cycle";

    private readonly Dictionary<string, TraitDef> traits;
    private readonly Interpreter interpreter;

    public ExpressionBook(IEnumerable<TraitDef> traits, IEnumerable<string> skills, IEnumerable<string> historyKinds, int historyCycles)
    {
        this.traits = traits.ToDictionary(t => t.Id, StringComparer.Ordinal);
        Skills = new HashSet<string>(skills, StringComparer.Ordinal);
        HistoryKinds = [.. historyKinds];
        HistoryCycles = historyCycles;

        // No type names, Math, Convert, or DateTime; no assignment; and the whitelist behind DynamicExpresso's reflection guard.
        interpreter = new Interpreter(InterpreterOptions.SystemKeywords).EnableAssignment(AssignmentOperators.None);
        interpreter.Visitors.Add(new Whitelist());
    }

    internal IReadOnlySet<string> Skills { get; }

    internal IReadOnlyList<string> HistoryKinds { get; }

    internal int HistoryCycles { get; }

    // The slots this expression may read, in reading order: for a slot's own expression, the slots before it and itself; for
    // requires, every slot. Null and an error when it is refused.
    public CheckedExpression? Check(string source, IReadOnlyList<string> slots, out ExpressionError? error)
    {
        CheckSlots(slots);
        Rewrite rewrite;
        try
        {
            rewrite = Rewrite.Run(source, slots, this);
        }
        catch (RewriteException e)
        {
            error = new ExpressionError(e.Message, e.Position);
            return null;
        }

        Lambda lambda;
        try
        {
            lambda = Parse(rewrite.Text, slots);
        }
        catch (ParseException e)
        {
            error = new ExpressionError(Clean(e.Message), rewrite.SourcePosition(e.Position));
            return null;
        }
        catch (RefusedException e)
        {
            int at = source.IndexOf(e.Name, StringComparison.Ordinal);
            error = new ExpressionError(e.Message, Math.Max(at, 0));
            return null;
        }

        if (lambda.ReturnType != typeof(bool))
        {
            error = new ExpressionError("An expression must be true or false.", 0);
            return null;
        }

        error = null;
        return new CheckedExpression(source, [.. slots], Clauses(lambda.Expression), rewrite.Traits, lambda);
    }

    // Cast: each of the expression's slots to a living crew member's id.
    public bool Evaluate(CheckedExpression expression, IStateSource state, IReadOnlyDictionary<string, string> cast)
    {
        IReadOnlyList<string> slots = expression.Slots;
        return expression.Compiled(
            slots.Count > 0 ? View(state, cast, slots[0]) : null,
            slots.Count > 1 ? View(state, cast, slots[1]) : null,
            slots.Count > 2 ? View(state, cast, slots[2]) : null,
            new ShipView(state),
            new CycleView(state));
    }

    internal TraitDef? Trait(string id)
    {
        return traits.GetValueOrDefault(id);
    }

    // The parser and the whitelist alone, on text as the rewrite would write it; null when it passes. For tests of the whitelist.
    internal string? ParseError(string rewritten, IReadOnlyList<string> slots)
    {
        try
        {
            Parse(rewritten, slots);
            return null;
        }
        catch (Exception e) when (e is ParseException or RefusedException)
        {
            return e.Message;
        }
    }

    // The fixed signature: three slots, the unbound ones named so that no expression can write them (the rewrite refuses a name
    // that is not a slot), then ship and cycle.
    private Lambda Parse(string text, IReadOnlyList<string> slots)
    {
        var parameters = new Parameter[MaxSlots + 2];
        for (int k = 0; k < MaxSlots; k++)
        {
            parameters[k] = new Parameter(k < slots.Count ? slots[k] : $"_{k}", typeof(CrewView));
        }

        parameters[MaxSlots] = new Parameter(Ship, typeof(ShipView));
        parameters[MaxSlots + 1] = new Parameter(Cycle, typeof(CycleView));
        return interpreter.Parse(text, parameters);
    }

    private static void CheckSlots(IReadOnlyList<string> slots)
    {
        if (slots.Count > MaxSlots)
        {
            throw new ArgumentException($"An expression reads at most {MaxSlots} slots.", nameof(slots));
        }

        if (slots.Any(s => s is Ship or Cycle || !SlotName().IsMatch(s)) || slots.Distinct(StringComparer.Ordinal).Count() != slots.Count)
        {
            throw new ArgumentException($"Slots must be distinct lower-case names, not {Ship} or {Cycle}: {string.Join(", ", slots)}.", nameof(slots));
        }
    }

    private static CrewView View(IStateSource state, IReadOnlyDictionary<string, string> cast, string slot)
    {
        return cast.TryGetValue(slot, out string? id) ? new CrewView(state, id) : throw new ArgumentException($"The cast binds no one to {slot}.", nameof(cast));
    }

    // Specificity (design 4.5): the clauses joined by && at the top of the tree.
    private static int Clauses(Expression expression)
    {
        return expression is BinaryExpression { NodeType: ExpressionType.AndAlso } and
            ? Clauses(and.Left) + Clauses(and.Right)
            : 1;
    }

    private static string Clean(string message)
    {
        string text = AtIndex().Replace(message, "").TrimEnd();
        return text.EndsWith('.') ? text : text + ".";
    }

    [GeneratedRegex(@"\s*\(at index \d+\)\.?\s*$")]
    private static partial Regex AtIndex();

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SlotName();
}

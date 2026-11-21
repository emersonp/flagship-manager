using System.Globalization;
using System.Linq.Expressions;

namespace Sim.Expressions;

// The ten shapes the editor's expression builder shows as rows (1e part six, design 4.5).
public enum ShapeKind
{
    Presence,
    Trait,
    PairTrait,
    Stat,
    Skill,
    HistoryLast,
    HistoryCount,
    ChronicleCount,
    StoryletSince,
    TraitSince,
}

// One clause as a row. Who: a slot name, or "ship" (a ship trait, the ship's chronicle, or a storylet's arrival anywhere on the
// ship). Name: aboard or fit, morale or loyalty, a trait, a skill, a history or chronicle kind, or a storylet. Toward: a pair
// trait's other slot. Op: one of == != >= <= > <. Value: a whole number, or, compared with == or != on a trait with named levels,
// the level's name as written (no quotes). Window: history.count's cycles or chronicle.count's days. Unit: "cycles" or "days", for
// the readings of time. Negated: !subject.fit, and !subject.history.last('hurt').
public sealed record Row(ShapeKind Kind, string Who, string? Name, string? Toward, string? Op, string? Value, int? Window, string? Unit, bool Negated);

// Rows null: the expression stays text, for the reason given ("uses ||"), or for the rewrite's error when it is refused.
public sealed record RowsRead(IReadOnlyList<Row>? Rows, string? Reason);

// An expression read into rows and rows written back (design 4.5). An expression is rows when it is clauses joined at the top by
// &&, as ExpressionBook.Split splits it, and each clause's parsed tree is one of the ten shapes. Reading goes through Check, so an
// expression that fits in rows is always one the loader accepts, and one it refuses is never rows. Nothing here stores rows: the
// YAML holds the string, and Write makes it in the house spelling (spaces around operators, single quotes, no parentheses).
public static class Shapes
{
    public const string UsesOr = "uses ||";

    public const string UsesArithmetic = "uses arithmetic";

    public const string GroupsNot = "groups with !( )";

    public const string ComparesReadings = "compares two readings";

    // Not in the plan's four (my call): a comparison written value first, a reading of cycle.number, true or false on its own, and
    // anything else valid that no row shows.
    public const string ValueFirst = "puts the value before the reading";

    public const string NoShape = "has a clause no row shows";

    private const string Ship = ExpressionBook.Ship;

    public static RowsRead Read(string expression, IReadOnlyList<string> slots, ExpressionBook book)
    {
        if (book.Check(expression, slots, out ExpressionError? error) is not { } whole)
        {
            return new RowsRead(null, error!.Message);
        }

        var rows = new List<Row>();
        foreach (CheckedExpression clause in book.Split(whole))
        {
            Expression tree = clause.Lambda.Expression;
            if (Reason(tree) is { } reason)
            {
                return new RowsRead(null, reason);
            }

            IReadOnlyList<string> levels = Rewrite.Run(clause.Source, clause.Slots, book).Levels;
            if (RowOf(tree, levels) is not { } row)
            {
                return new RowsRead(null, Fallback(tree));
            }

            rows.Add(row);
        }

        return new RowsRead(rows, null);
    }

    public static string Write(IReadOnlyList<Row> rows)
    {
        return string.Join(" && ", rows.Select(Clause));
    }

    private static string Clause(Row row)
    {
        string compared = $"{row.Op} {row.Value}";
        if (row.Kind is not (ShapeKind.Presence or ShapeKind.HistoryLast))
        {
            Need(row.Op, nameof(Row.Op), row);
            Need(row.Value, nameof(Row.Value), row);
            compared = IsNumber(row.Value!) ? $"{row.Op} {row.Value}" : $"{row.Op} '{row.Value}'";
        }
        else if (row.Op is not null || row.Value is not null)
        {
            throw new ArgumentException($"A {row.Kind} row compares nothing: {row}.", nameof(row));
        }

        if (row.Negated && row.Kind is not (ShapeKind.Presence or ShapeKind.HistoryLast))
        {
            throw new ArgumentException($"Only a Presence or HistoryLast row is negated: {row}.", nameof(row));
        }

        string not = row.Negated ? "!" : "";
        string name = Need(row.Name, nameof(Row.Name), row);
        return row.Kind switch
        {
            ShapeKind.Presence => $"{not}{row.Who}.{name}",
            ShapeKind.Trait => $"{row.Who}.trait.{name} {compared}",
            ShapeKind.PairTrait => $"{row.Who}.trait.{name}.toward({Need(row.Toward, nameof(Row.Toward), row)}) {compared}",
            ShapeKind.Stat => $"{row.Who}.{name} {compared}",
            ShapeKind.Skill => $"{row.Who}.skills.{name} {compared}",
            ShapeKind.HistoryLast => $"{not}{row.Who}.history.last('{name}')",
            ShapeKind.HistoryCount => $"{row.Who}.history.count('{name}', {Window(row)}) {compared}",
            ShapeKind.ChronicleCount => $"{(row.Who == Ship ? "" : row.Who + ".")}chronicle.count('{name}', {Window(row)}) {compared}",
            ShapeKind.StoryletSince => $"storylet.{name}.{Since(row)} {compared}",
            ShapeKind.TraitSince => $"{row.Who}.trait.{name}.{Since(row)} {compared}",
            _ => throw new ArgumentException($"No shape {row.Kind}.", nameof(row)),
        };
    }

    private static string Need(string? part, string what, Row row)
    {
        return string.IsNullOrEmpty(part) ? throw new ArgumentException($"A {row.Kind} row needs its {what}: {row}.", nameof(row)) : part;
    }

    private static string Window(Row row)
    {
        return row.Window is { } window ? window.ToString(CultureInfo.InvariantCulture) : throw new ArgumentException($"A {row.Kind} row needs its window: {row}.", nameof(row));
    }

    private static string Since(Row row)
    {
        return row.Unit switch
        {
            "cycles" => "cyclesSince",
            "days" => "daysSince",
            _ => throw new ArgumentException($"A {row.Kind} row's unit is cycles or days: {row}.", nameof(row)),
        };
    }

    private static bool IsNumber(string value)
    {
        return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    // The first reason in reading order a valid clause stays text, or null: || and arithmetic where they are written, ! before
    // anything but a reading of true or false, and a comparison of two readings.
    private static string? Reason(Expression node)
    {
        switch (node)
        {
            case BinaryExpression { NodeType: ExpressionType.OrElse } or:
                return Reason(or.Left) ?? UsesOr;
            case BinaryExpression { NodeType: ExpressionType.AndAlso } and:
                return Reason(and.Left) ?? Reason(and.Right);
            case BinaryExpression { NodeType: ExpressionType.Add or ExpressionType.AddChecked or ExpressionType.Subtract or ExpressionType.SubtractChecked or ExpressionType.Multiply or ExpressionType.MultiplyChecked } arithmetic:
                return Reason(arithmetic.Left) ?? UsesArithmetic;
            case BinaryExpression binary:
                return Reason(binary.Left) ?? (IsReading(binary.Left) && IsReading(binary.Right) ? ComparesReadings : Reason(binary.Right));
            case UnaryExpression { NodeType: ExpressionType.Not } not:
                return Truth(not.Operand) is null ? GroupsNot : null;
            case UnaryExpression { NodeType: ExpressionType.Negate or ExpressionType.NegateChecked } negate:
                return Number(negate) is null ? UsesArithmetic : null;
            case UnaryExpression unary:
                return Reason(unary.Operand);
            default:
                return null;
        }
    }

    private static string Fallback(Expression node)
    {
        return node is BinaryExpression binary && Number(binary.Left) is not null && IsReading(binary.Right) ? ValueFirst : NoShape;
    }

    // Anything read from the state, as opposed to a constant.
    private static bool IsReading(Expression node)
    {
        return Number(node) is null && Unwrap(node) is not ConstantExpression;
    }

    private static Expression Unwrap(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.UnaryPlus } unary)
        {
            node = unary.Operand;
        }

        return node;
    }

    // A whole number as written: a constant, or a negated one.
    private static int? Number(Expression node)
    {
        return Unwrap(node) switch
        {
            ConstantExpression { Value: int number } => number,
            UnaryExpression { NodeType: ExpressionType.Negate or ExpressionType.NegateChecked } negate when Unwrap(negate.Operand) is ConstantExpression { Value: int number } => -number,
            _ => null,
        };
    }

    private static Row? RowOf(Expression node, IReadOnlyList<string> levels)
    {
        if (node is UnaryExpression { NodeType: ExpressionType.Not } not)
        {
            return Truth(not.Operand) is { } truth ? truth with { Negated = true } : null;
        }

        if (Truth(node) is { } reading)
        {
            return reading;
        }

        if (node is not BinaryExpression binary || OpOf(binary.NodeType) is not { } op || Number(binary.Right) is not { } number)
        {
            return null;
        }

        if (Compared(Unwrap(binary.Left)) is not { } row)
        {
            return null;
        }

        // A level compared by name: the rewrite resolved it to its number, and kept the name as written.
        string value = row.Kind is ShapeKind.Trait or ShapeKind.PairTrait && op is "==" or "!=" && levels.Count == 1
            ? levels[0]
            : number.ToString(CultureInfo.InvariantCulture);
        return row with { Op = op, Value = value };
    }

    // A reading that is true or false on its own: aboard, fit, and history.last.
    private static Row? Truth(Expression node)
    {
        return Unwrap(node) switch
        {
            MemberExpression { Member.Name: "aboard" or "fit", Expression: ParameterExpression slot } member when slot.Type == typeof(CrewView) =>
                Shape(ShapeKind.Presence, slot.Name!, member.Member.Name),
            MethodCallExpression { Method.Name: "last", Object: MemberExpression { Member.Name: "history", Expression: ParameterExpression slot } } call
                when call.Arguments is [ConstantExpression { Value: string kind }] => Shape(ShapeKind.HistoryLast, slot.Name!, kind),
            _ => null,
        };
    }

    // A reading compared with a number, without its operator and value.
    private static Row? Compared(Expression node)
    {
        switch (node)
        {
            case MemberExpression { Member.Name: "morale" or "loyalty", Expression: ParameterExpression slot } member when slot.Type == typeof(CrewView):
                return Shape(ShapeKind.Stat, slot.Name!, member.Member.Name);
            case IndexExpression { Object: MemberExpression { Member.Name: "skills", Expression: ParameterExpression slot } } index
                when index.Arguments is [ConstantExpression { Value: string skill }]:
                return Shape(ShapeKind.Skill, slot.Name!, skill);
            case IndexExpression { Object: MemberExpression { Member.Name: "trait", Expression: ParameterExpression holder } } index
                when index.Arguments is [ConstantExpression { Value: string trait }]:
                return Shape(ShapeKind.Trait, holder.Name!, trait);
            case MethodCallExpression { Method.Name: "toward", Object: MemberExpression { Member.Name: "trait", Expression: ParameterExpression holder } } call
                when call.Arguments is [ConstantExpression { Value: string trait }, ParameterExpression target]:
                return Shape(ShapeKind.PairTrait, holder.Name!, trait) with { Toward = target.Name };
            case MethodCallExpression { Method.Name: "cyclesSince" or "daysSince", Object: MemberExpression { Member.Name: "trait", Expression: ParameterExpression holder } } call
                when call.Arguments is [ConstantExpression { Value: string trait }]:
                return Shape(ShapeKind.TraitSince, holder.Name!, trait) with { Unit = UnitOf(call) };
            case MethodCallExpression { Method.Name: "cyclesSince" or "daysSince", Object: ParameterExpression { Name: ExpressionBook.Storylet } } call
                when call.Arguments is [ConstantExpression { Value: string storylet }]:
                return Shape(ShapeKind.StoryletSince, Ship, storylet) with { Unit = UnitOf(call) };
            case MethodCallExpression { Method.Name: "count", Object: MemberExpression { Member.Name: "history", Expression: ParameterExpression slot } } call
                when call.Arguments is [ConstantExpression { Value: string kind }, ConstantExpression { Value: int cycles }]:
                return Shape(ShapeKind.HistoryCount, slot.Name!, kind) with { Window = cycles };
            case MethodCallExpression { Method.Name: "count", Object: ParameterExpression { Name: ExpressionBook.Chronicle } } call
                when call.Arguments is [ConstantExpression { Value: string kind }, ConstantExpression { Value: int days }]:
                return Shape(ShapeKind.ChronicleCount, Ship, kind) with { Window = days };
            case MethodCallExpression { Method.Name: "count", Object: MemberExpression { Member.Name: "chronicle", Expression: ParameterExpression slot } } call
                when call.Arguments is [ConstantExpression { Value: string kind }, ConstantExpression { Value: int days }]:
                return Shape(ShapeKind.ChronicleCount, slot.Name!, kind) with { Window = days };
            default:
                return null;
        }
    }

    private static Row Shape(ShapeKind kind, string who, string name)
    {
        return new Row(kind, who, name, null, null, null, null, null, false);
    }

    private static string UnitOf(MethodCallExpression call)
    {
        return call.Method.Name == "cyclesSince" ? "cycles" : "days";
    }

    private static string? OpOf(ExpressionType type)
    {
        return type switch
        {
            ExpressionType.Equal => "==",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.LessThan => "<",
            _ => null,
        };
    }
}

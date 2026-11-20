using System.Linq.Expressions;
using System.Reflection;

namespace Sim.Expressions;

// Runs inside DynamicExpresso's Parse (Interpreter.Visitors) behind the rewrite, and refuses any node outside the language on its
// own: DynamicExpresso's reflection guard lets GetType(), ToString(), a string's Length, and / through (design section 9, Task 1).
// Members and methods only on the view types; constants only int, bool, and string; arithmetic only +, -, and *.
internal sealed class Whitelist : ExpressionVisitor
{
    private static readonly HashSet<Type> Views =
        [typeof(CrewView), typeof(SkillsView), typeof(TraitsView), typeof(HistoryView), typeof(ShipView), typeof(ShipTraitsView), typeof(CycleView), typeof(StoryletsView)];

    // History's count and last, a pair trait's toward, and the readings of time on a trait or a storylet (slice 1e-calendar). The
    // indexer (x.trait["t"], x.skills["S"]) is an Index node.
    private static readonly HashSet<string> Methods = ["count", "last", "toward", "cyclesSince", "daysSince"];

    private static readonly HashSet<Type> Constants = [typeof(int), typeof(bool), typeof(string)];

    // A conversion may also lift a number to a reading of time's int?, so that it compares with one.
    private static readonly HashSet<Type> Converts = [typeof(int), typeof(bool), typeof(string), typeof(int?)];

    private static readonly HashSet<ExpressionType> Nodes =
    [
        ExpressionType.Parameter, ExpressionType.Constant, ExpressionType.MemberAccess, ExpressionType.Index, ExpressionType.Call,
        ExpressionType.AndAlso, ExpressionType.OrElse, ExpressionType.Not,
        ExpressionType.Equal, ExpressionType.NotEqual, ExpressionType.LessThan, ExpressionType.LessThanOrEqual, ExpressionType.GreaterThan, ExpressionType.GreaterThanOrEqual,
        ExpressionType.Add, ExpressionType.Subtract, ExpressionType.Multiply, ExpressionType.Negate, ExpressionType.UnaryPlus, ExpressionType.Convert,
    ];

    public override Expression? Visit(Expression? node)
    {
        if (node is not null && !Nodes.Contains(node.NodeType))
        {
            throw new RefusedException(node.NodeType.ToString());
        }

        return base.Visit(node);
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        if (node.Value is null || !Constants.Contains(node.Type))
        {
            throw new RefusedException(node.Value?.ToString() ?? "null");
        }

        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Member is not PropertyInfo || !Views.Contains(node.Member.DeclaringType!))
        {
            throw new RefusedException(node.Member.Name);
        }

        return base.VisitMember(node);
    }

    // A view's method, with the trait, skill, or history kind a constant, as the rewrite writes it.
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!Views.Contains(node.Method.DeclaringType!) || !Methods.Contains(node.Method.Name) || node.Arguments[0] is not ConstantExpression)
        {
            throw new RefusedException(node.Method.Name);
        }

        if (node.Method.Name == "count" && node.Arguments[1] is not ConstantExpression)
        {
            throw new RefusedException("count");
        }

        return base.VisitMethodCall(node);
    }

    // x.trait["t"] and x.skills["S"]: a view's indexer, with a constant id.
    protected override Expression VisitIndex(IndexExpression node)
    {
        if (node.Indexer is null || !Views.Contains(node.Indexer.DeclaringType!) || node.Arguments is not [ConstantExpression])
        {
            throw new RefusedException(node.Indexer?.Name ?? "[]");
        }

        return base.VisitIndex(node);
    }

    // == and != on two crew members compare who they are, through the view's own operator; no other operator method.
    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.Method is not null && node.Method.DeclaringType != typeof(CrewView))
        {
            throw new RefusedException(node.Method.Name);
        }

        return base.VisitBinary(node);
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.Method is not null || (node.NodeType == ExpressionType.Convert && !(Converts.Contains(node.Type) && Converts.Contains(node.Operand.Type))))
        {
            throw new RefusedException(node.Method?.Name ?? node.Type.Name);
        }

        return base.VisitUnary(node);
    }
}

// Name: what was refused, which ExpressionBook looks for in the source to place the error (the tree holds no positions).
internal sealed class RefusedException(string name) : Exception($"{name} is not part of the expression language.")
{
    public string Name { get; } = name;
}

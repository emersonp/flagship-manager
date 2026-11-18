using System.Linq.Expressions;
using Sim.Core.Content;

namespace Sim.Expressions;

// An expression read with only its traits known (design 4.6, "no hidden memory"): true or false when the traits decide it, null
// when it turns on anything else (a stat, a skill, the history, fitness, being aboard, the cycle, or who is who). && and || follow
// three-valued logic, so a clause on a stat counts as "may still hold" and a false trait clause still makes the whole false. It
// walks the parsed tree; nothing is compiled, so the loader can afford it on every save.
public sealed partial class ExpressionBook
{
    public bool? OnTraits(CheckedExpression expression, Func<TraitRead, int> trait)
    {
        Value value = new TraitReader(trait).Read(expression.Lambda.Expression);
        return value.Known ? value.Number != 0 : null;
    }

    // An int or a bool (as 1 or 0), or unknown.
    private readonly record struct Value(bool Known, int Number)
    {
        public static readonly Value Unknown = new(false, 0);

        public static Value Of(int number)
        {
            return new Value(true, number);
        }

        public static Value Of(bool truth)
        {
            return new Value(true, truth ? 1 : 0);
        }
    }

    private sealed class TraitReader(Func<TraitRead, int> trait)
    {
        public Value Read(Expression node)
        {
            switch (node)
            {
                case ConstantExpression { Value: int number }:
                    return Value.Of(number);
                case ConstantExpression { Value: bool truth }:
                    return Value.Of(truth);
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.UnaryPlus } unary:
                    return Read(unary.Operand);
                case UnaryExpression { NodeType: ExpressionType.Not } not:
                    return Read(not.Operand) is { Known: true } operand ? Value.Of(operand.Number == 0) : Value.Unknown;
                case UnaryExpression { NodeType: ExpressionType.Negate or ExpressionType.NegateChecked } negate:
                    return Read(negate.Operand) is { Known: true } negated ? Value.Of(-negated.Number) : Value.Unknown;
                case BinaryExpression { NodeType: ExpressionType.AndAlso } and:
                    return And(Read(and.Left), Read(and.Right));
                case BinaryExpression { NodeType: ExpressionType.OrElse } or:
                    return Or(Read(or.Left), Read(or.Right));
                case BinaryExpression binary when binary.Left.Type == typeof(CrewView) || binary.Right.Type == typeof(CrewView):
                    // Who is who is fixed by the cast, not by any trait.
                    return Value.Unknown;
                case BinaryExpression binary:
                    return Binary(binary.NodeType, Read(binary.Left), Read(binary.Right));
                case IndexExpression { Object: MemberExpression { Member.Name: "trait", Expression: ParameterExpression holder } } index
                    when index.Arguments is [ConstantExpression { Value: string id }]:
                    return Value.Of(trait(new TraitRead(id, holder.Name!, null)));
                case MethodCallExpression { Method.Name: "toward", Object: MemberExpression { Member.Name: "trait", Expression: ParameterExpression holder } } call
                    when call.Arguments is [ConstantExpression { Value: string id }, ParameterExpression target]:
                    return Value.Of(trait(new TraitRead(id, holder.Name!, target.Name)));
                default:
                    return Value.Unknown;
            }
        }

        private static Value And(Value left, Value right)
        {
            if ((left.Known && left.Number == 0) || (right.Known && right.Number == 0))
            {
                return Value.Of(false);
            }

            return left.Known && right.Known ? Value.Of(true) : Value.Unknown;
        }

        private static Value Or(Value left, Value right)
        {
            if ((left.Known && left.Number != 0) || (right.Known && right.Number != 0))
            {
                return Value.Of(true);
            }

            return left.Known && right.Known ? Value.Of(false) : Value.Unknown;
        }

        private static Value Binary(ExpressionType type, Value left, Value right)
        {
            if (!left.Known || !right.Known)
            {
                return Value.Unknown;
            }

            (int a, int b) = (left.Number, right.Number);
            return type switch
            {
                ExpressionType.Equal => Value.Of(a == b),
                ExpressionType.NotEqual => Value.Of(a != b),
                ExpressionType.LessThan => Value.Of(a < b),
                ExpressionType.LessThanOrEqual => Value.Of(a <= b),
                ExpressionType.GreaterThan => Value.Of(a > b),
                ExpressionType.GreaterThanOrEqual => Value.Of(a >= b),
                ExpressionType.Add or ExpressionType.AddChecked => Value.Of(a + b),
                ExpressionType.Subtract or ExpressionType.SubtractChecked => Value.Of(a - b),
                ExpressionType.Multiply or ExpressionType.MultiplyChecked => Value.Of(a * b),
                _ => Value.Unknown,
            };
        }
    }
}

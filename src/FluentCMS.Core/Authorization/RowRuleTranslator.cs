using System.Linq.Expressions;
using System.Reflection;
using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// Content tables are property bags with no CLR rows, so a rule's predicate never runs. It is read once, at startup,
// into the filter AST: a member chain of the content class becomes an API path, and whatever does not touch the row is
// a value, computed per operation. A predicate outside these shapes is refused rather than run in memory:
//   x.Field == value, !=, <, <=, >, >=, in either order, and x.Field == null
//   x.Flag, !x.Flag, &&, ||, !
//   x.Text.Contains(value), x.Text.StartsWith(value), x.List.Contains(value), values.Contains(x.Field)
internal sealed class RowRuleTranslator
{
    private readonly string _name;
    private readonly ParameterExpression _context;
    private readonly ParameterExpression _row;
    private readonly List<RowRuleCondition> _conditions = [];
    private readonly List<Func<OperationContext, object?>> _values = [];

    private RowRuleTranslator(string name, ParameterExpression context, ParameterExpression row)
    {
        _name = name;
        _context = context;
        _row = row;
    }

    public static RowRule Translate<T>(string name, string? typeName, Expression<Func<OperationContext, Expression<Func<T, bool>>>> rule)
        where T : ContentItem
    {
        if (rule.Body is not UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression predicate })
        {
            throw Refuse(name, rule.Body, "write the rule as ctx => row => condition");
        }

        var translator = new RowRuleTranslator(name, rule.Parameters[0], predicate.Parameters[0]);
        var build = translator.Node(predicate.Body);
        return new RowRule(name, typeName, translator._conditions, translator._values, build);
    }

    private Func<object?[], ContentFilter> Node(Expression expression)
    {
        switch (expression.NodeType)
        {
            case ExpressionType.AndAlso or ExpressionType.And:
                var (allLeft, allRight) = Both((BinaryExpression)expression);
                return values => new And(allLeft(values), allRight(values));
            case ExpressionType.OrElse or ExpressionType.Or:
                var (anyLeft, anyRight) = Both((BinaryExpression)expression);
                return values => new Or(anyLeft(values), anyRight(values));
            case ExpressionType.Not:
                var operand = ((UnaryExpression)expression).Operand;
                if (TryPath(operand, out var flag, out _))
                {
                    return Constant(flag, FilterOperator.Equal, false);
                }

                var negated = Node(operand);
                return values => new Not(negated(values));
            case ExpressionType.Convert:
                return Node(((UnaryExpression)expression).Operand);
            case ExpressionType.Equal:
                return Comparison((BinaryExpression)expression, FilterOperator.Equal, FilterOperator.Equal);
            case ExpressionType.NotEqual:
                return Comparison((BinaryExpression)expression, FilterOperator.NotEqual, FilterOperator.NotEqual);
            case ExpressionType.LessThan:
                return Comparison((BinaryExpression)expression, FilterOperator.LessThan, FilterOperator.GreaterThan);
            case ExpressionType.LessThanOrEqual:
                return Comparison((BinaryExpression)expression, FilterOperator.LessThanOrEqual, FilterOperator.GreaterThanOrEqual);
            case ExpressionType.GreaterThan:
                return Comparison((BinaryExpression)expression, FilterOperator.GreaterThan, FilterOperator.LessThan);
            case ExpressionType.GreaterThanOrEqual:
                return Comparison((BinaryExpression)expression, FilterOperator.GreaterThanOrEqual, FilterOperator.LessThanOrEqual);
            case ExpressionType.MemberAccess when TryPath(expression, out var field, out _):
                return Constant(field, FilterOperator.Equal, true);
            case ExpressionType.Call:
                return Call((MethodCallExpression)expression);
            default:
                throw Refuse(_name, expression, "a rule is made of comparisons on fields of the entry, joined by &&, || and !");
        }
    }

    private (Func<object?[], ContentFilter> Left, Func<object?[], ContentFilter> Right) Both(BinaryExpression expression) =>
        (Node(expression.Left), Node(expression.Right));

    private Func<object?[], ContentFilter> Comparison(BinaryExpression comparison, FilterOperator asWritten, FilterOperator reversed)
    {
        var (path, type, value, filterOperator) = TryPath(comparison.Left, out var leftPath, out var leftType)
            ? (leftPath, leftType, comparison.Right, asWritten)
            : TryPath(comparison.Right, out var rightPath, out var rightType)
                ? (rightPath, rightType, comparison.Left, reversed)
                : throw Refuse(_name, comparison, "one side must be a field of the entry");

        if (!IsValue(value))
        {
            throw Refuse(_name, comparison, "the other side must not read the entry");
        }

        if (Unwrap(value) is ConstantExpression { Value: null })
        {
            return filterOperator switch
            {
                FilterOperator.Equal => Constant(path, FilterOperator.IsNull, null),
                FilterOperator.NotEqual => Constant(path, FilterOperator.IsNotNull, null),
                _ => throw Refuse(_name, comparison, "null can only be compared with == or !="),
            };
        }

        return Bound(path, filterOperator, value, type);
    }

    private Func<object?[], ContentFilter> Call(MethodCallExpression call)
    {
        var (source, item) = call switch
        {
            { Object: { } instance, Arguments.Count: 1 } => (instance, call.Arguments[0]),
            { Object: null, Arguments.Count: 2 } => (call.Arguments[0], call.Arguments[1]),
            _ => throw Refuse(_name, call, "only Contains and StartsWith with one value can be translated"),
        };

        switch (call.Method.Name)
        {
            case nameof(string.Contains) when TryPath(source, out var list, out _) && IsValue(item) && item.Type != typeof(char):
                return Bound(list, FilterOperator.Contains, item, typeof(object));
            case nameof(string.Contains) when TryPath(item, out var field, out _) && IsValue(source) && !IsText(source):
                return Bound(field, FilterOperator.In, SpanSource(source), typeof(object));
            case nameof(string.StartsWith) when TryPath(source, out var text, out _) && IsValue(item) && item.Type != typeof(char):
                return Bound(text, FilterOperator.StartsWith, item, typeof(object));
            default:
                throw Refuse(_name, call, "only Contains and StartsWith with one value can be translated, and a list of values cannot be text");
        }
    }

    // text.Contains(x.Field) looks for a fragment, where values.Contains(x.Field) looks for a member, and only the second
    // is what In means. Reading text as a list of one would turn a deny-list into nothing.
    private static bool IsText(Expression source) => typeof(IEnumerable<char>).IsAssignableFrom(Unwrap(source).Type);

    // C# 14 binds array.Contains(x) to MemoryExtensions over an implicit span, which cannot be boxed. The array can.
    private static Expression SpanSource(Expression source) =>
        source is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } conversion ? conversion.Arguments[0] : source;

    private Func<object?[], ContentFilter> Constant(string path, FilterOperator filterOperator, object? value)
    {
        _conditions.Add(new RowRuleCondition(path, filterOperator));
        var filter = new FieldFilter(path, filterOperator, value);
        return _ => filter;
    }

    private Func<object?[], ContentFilter> Bound(string path, FilterOperator filterOperator, Expression value, Type fieldType)
    {
        _conditions.Add(new RowRuleCondition(path, filterOperator));

        var read = Expression.Lambda<Func<OperationContext, object?>>(Expression.Convert(value, typeof(object)), _context).Compile();
        var enumType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        // An expression tree compares an enum as its underlying number.
        _values.Add(enumType.IsEnum
            ? context => read(context) is { } number ? Enum.ToObject(enumType, number) : null
            : read);

        var index = _values.Count - 1;
        return values => new FieldFilter(path, filterOperator, values[index]);
    }

    private bool TryPath(Expression expression, out string path, out Type type)
    {
        var inner = Unwrap(expression);
        type = inner.Type;
        path = "";

        List<string> segments = [];
        while (inner is MemberExpression { Member: PropertyInfo property, Expression: { } owner })
        {
            segments.Add(SchemaNaming.ToCamelCase(property.Name));
            inner = Unwrap(owner);
        }

        if (inner != _row || segments.Count == 0)
        {
            return false;
        }

        segments.Reverse();
        path = string.Join('.', segments);
        return true;
    }

    private bool IsValue(Expression expression)
    {
        var finder = new RowFinder(_row);
        finder.Visit(expression);
        return !finder.Found;
    }

    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
        {
            expression = conversion.Operand;
        }

        return expression;
    }

    private static FluentCmsConfigurationException Refuse(string name, Expression expression, string reason) => new(
        AuthorizationErrorCodes.UnsupportedRowRule,
        $"Row rule '{name}' cannot be translated to a filter at '{expression}': {reason}.");

    private sealed class RowFinder(ParameterExpression row) : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= node == row;
            return node;
        }
    }
}

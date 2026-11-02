using System.Globalization;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Validation;

public static class ConditionEvaluator
{
    public static bool Evaluate(Condition condition, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(values);

        switch (condition.Operator)
        {
            case ConditionOperator.Equal:
                return Matches(Get(values, condition), condition.Values[0]);
            case ConditionOperator.In:
                var actual = Get(values, condition);
                return condition.Values.Any(expected => Matches(actual, expected));
            case ConditionOperator.IsEmpty:
                return FieldValue.IsEmpty(Get(values, condition));
            case ConditionOperator.And:
                return condition.Operands.All(operand => Evaluate(operand, values));
            case ConditionOperator.Or:
                return condition.Operands.Any(operand => Evaluate(operand, values));
            case ConditionOperator.Not:
                return !Evaluate(condition.Operands[0], values);
            default:
                throw new ArgumentException($"Unknown condition operator {condition.Operator}.", nameof(condition));
        }
    }

    // An unknown field is null.
    private static object? Get(IReadOnlyDictionary<string, object?> values, Condition condition) =>
        condition.FieldName is { } name && values.TryGetValue(name, out var value) ? value : null;

    // A list matches when one of its items does (a multi select "equals" a value it contains), and an empty list matches null.
    private static bool Matches(object? actual, object? expected)
    {
        if (FieldValue.Items(actual) is { } items)
        {
            var list = items.ToList();
            return expected is null ? list.Count == 0 : list.Any(item => Same(FieldValue.Normalize(item), expected));
        }

        return Same(FieldValue.Normalize(actual), expected);
    }

    private static bool Same(object? actual, object? expected) => (actual, expected) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (string left, string right) => string.Equals(left, right, StringComparison.Ordinal),
        (bool left, bool right) => left == right,
        (long or decimal, long or decimal) => Convert.ToDecimal(actual, CultureInfo.InvariantCulture) == Convert.ToDecimal(expected, CultureInfo.InvariantCulture),
        _ => false,
    };
}

using System.Collections;
using FluentCMS.Content;

namespace FluentCMS.Core.Authorization;

// A row rule in the form the query engine takes it: a filter over the API paths of a type. TypeName is null for a rule
// on ContentItem, which holds for every type and can name only system fields. Conditions is what the rule compares,
// which the registry checks against the schema at startup.
internal sealed class RowRule(
    string name,
    string? typeName,
    IReadOnlyList<RowRuleCondition> conditions,
    IReadOnlyList<Func<OperationContext, object?>> values,
    Func<object?[], ContentFilter> build)
{
    // No entry has the empty id, so this matches nothing, and the AST needs no node for false.
    public static readonly ContentFilter NoRows = new FieldFilter("id", FilterOperator.Equal, Guid.Empty);

    public string Name => name;

    public string? TypeName => typeName;

    public IReadOnlyList<RowRuleCondition> Conditions => conditions;

    // A value the operation does not have, such as a claim the principal lacks, matches no row instead of comparing
    // with null, which would show that principal every row with no region. Empty text counts as missing, because every
    // value contains and starts with it. The empty id counts because it is both the anonymous principal's id and the
    // createdBy of what the system principal wrote. Each of them closes the whole rule, not one comparison, so that a
    // Not around the comparison cannot turn it into every row, and one inside a list closes it the same way.
    public ContentFilter For(OperationContext context)
    {
        var resolved = new object?[values.Count];
        for (var index = 0; index < resolved.Length; index++)
        {
            var value = values[index](context);
            if (value is IEnumerable items and not string)
            {
                var list = items.Cast<object?>().ToList();
                if (list.Count == 0 || list.Exists(IsMissing))
                {
                    return NoRows;
                }

                value = list;
            }
            else if (IsMissing(value))
            {
                return NoRows;
            }

            resolved[index] = value;
        }

        return build(resolved);
    }

    private static bool IsMissing(object? value) => value is null or "" || (value is Guid id && id == Guid.Empty);
}

internal sealed record RowRuleCondition(string Path, FilterOperator Operator);

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
    // with null, which would show that principal every row with no region. The empty id counts as missing because it
    // is both the anonymous principal's id and the createdBy of what the system principal wrote. It closes the whole
    // rule, not one comparison, so that a Not around the comparison cannot turn it into every row.
    public ContentFilter For(OperationContext context)
    {
        var resolved = new object?[values.Count];
        for (var index = 0; index < resolved.Length; index++)
        {
            var value = values[index](context);
            if (value is IEnumerable items and not string)
            {
                value = items.Cast<object?>().Where(item => item is not null).ToList();
            }

            if (value is null or ICollection { Count: 0 } || (value is Guid id && id == Guid.Empty))
            {
                return NoRows;
            }

            resolved[index] = value;
        }

        return build(resolved);
    }
}

internal sealed record RowRuleCondition(string Path, FilterOperator Operator);

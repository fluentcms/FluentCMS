using FluentCMS.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Core.Storage;
using FluentCMS.Schema;

namespace FluentCMS.Core.Authorization;

// The row rules by name. A rule for a type wins over one on ContentItem of the same name. Building this checks every
// rule against the schema, and RowRuleStartupCheck builds it when the host starts.
internal sealed class RowRuleRegistry
{
    private readonly Dictionary<(string Name, string? Type), RowRule> _rules = [];

    public RowRuleRegistry(SchemaModel schema, ContentQueryOptions options, IEnumerable<RowRule> rules)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rules);

        var paths = new QueryPaths(schema, new ContentQueryAccess.Closed());
        foreach (var rule in rules)
        {
            Check(schema, options, paths, rule);
            if (!_rules.TryAdd((rule.Name, rule.TypeName), rule))
            {
                throw new FluentCmsConfigurationException(
                    AuthorizationErrorCodes.DuplicateRowRule,
                    $"Row rule '{rule.Name}' is registered twice for {rule.TypeName ?? "every content type"}.");
            }
        }
    }

    public RowRule? Find(string name, string typeName) =>
        _rules.GetValueOrDefault((name, typeName)) ?? _rules.GetValueOrDefault((name, null));

    private static void Check(SchemaModel schema, ContentQueryOptions options, QueryPaths paths, RowRule rule)
    {
        var type = rule.TypeName is null ? null : schema.FindContentType(rule.TypeName) ?? throw new FluentCmsConfigurationException(
            AuthorizationErrorCodes.InvalidRowRule,
            $"Row rule '{rule.Name}' is registered for '{rule.TypeName}', which is not a content type of the schema.");

        foreach (var condition in rule.Conditions)
        {
            var allowed = type is null
                ? SystemField.Find(condition.Path)?.Operators.Contains(condition.Operator) ?? false
                : Allows(schema, options, paths, type, condition.Path, condition.Operator, hops: 0);

            if (!allowed)
            {
                throw new FluentCmsConfigurationException(
                    AuthorizationErrorCodes.InvalidRowRule,
                    $"Row rule '{rule.Name}' uses {condition.Operator} on '{condition.Path}' of {rule.TypeName ?? "every content type"}, which is not a field that allows it.");
            }
        }
    }

    private static bool Allows(SchemaModel schema, ContentQueryOptions options, QueryPaths paths, SchemaType type, string path, FilterOperator filterOperator, int hops)
    {
        switch (paths.Resolve(type, path, trusted: true))
        {
            case SystemPath system:
                return system.Field.Operators.Contains(filterOperator);
            case FieldPath { Rest.Length: 0 } field:
                return schema.FieldTypes.Find(field.Stored.Field.FieldType)!.FilterOperators.Contains(filterOperator);
            case FieldPath { Stored.Kind: FieldStorageKind.Reference, Stored.Field.Relation: { } relation } hop when hops < options.MaxPopulateDepth:
                return schema.FindContentType(relation.Target) is { } target && Allows(schema, options, paths, target, string.Join('.', hop.Rest), filterOperator, hops + 1);
            case FieldPath { Stored.Kind: FieldStorageKind.Json, Rest.Length: 1, Stored.Field.FieldType: FieldTypeNames.Json }:
                return filterOperator is FilterOperator.Equal or FilterOperator.NotEqual or FilterOperator.Contains;
            default:
                return false;
        }
    }
}

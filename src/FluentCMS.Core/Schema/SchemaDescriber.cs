using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// A model as the plain records of the SDK, which the Management API serves and a client reads back. Nothing is left out
// here: SchemaIntrospection narrows the model first.
internal static class SchemaDescriber
{
    public static SchemaDescription Describe(SchemaModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new SchemaDescription
        {
            Types = [.. model.ContentTypes.Select(Describe)],
            Components = [.. model.Components.Select(Describe)],
            Blocks = [.. model.Blocks.Select(Describe)],
        };
    }

    public static SchemaTypeDescription Describe(SchemaType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var isContentType = type.Kind is SchemaTypeKind.Collection or SchemaTypeKind.Singleton;
        return new SchemaTypeDescription
        {
            Name = type.Name,
            Kind = type.Kind,
            Localized = type.Localized,
            Scope = isContentType ? type.Scope : null,
            UnpublishedReferences = isContentType ? type.UnpublishedReferences : null,
            MaxVersions = isContentType ? type.MaxVersions : null,
            TrashRetentionDays = isContentType ? type.TrashRetentionDays : null,
            Fields = [.. type.Fields.Select(Describe)],
            ReverseRelations = [.. type.ReverseRelations.Select(relation => new ReverseRelationDescription
            {
                Name = relation.Name,
                SourceType = relation.SourceType,
                SourceField = relation.SourceField,
            })],
        };
    }

    private static SchemaFieldDescription Describe(SchemaField field) => new()
    {
        Name = field.Name,
        Type = field.FieldType,
        Multiple = field.Multiple,
        Localizable = field.Localizable,
        Indexed = field.Indexed,
        Searchable = field.Searchable,
        ReadOnly = field.ReadOnly,
        Hidden = field.Hidden,
        FormerNames = field.FormerNames,
        Display = field.Display is { } display
            ? new FieldDisplayDescription
            {
                Label = display.Label,
                Help = display.Help,
                Placeholder = display.Placeholder,
                Group = display.Group,
                Tab = display.Tab,
                Width = display.Width,
            }
            : null,
        DefaultValue = field.DefaultValue,
        Validation = new FieldValidationDescription
        {
            Required = field.Required,
            Unique = field.Unique,
            MinLength = field.MinLength,
            MaxLength = field.MaxLength,
            Minimum = field.Minimum,
            Maximum = field.Maximum,
            Pattern = field.Pattern,
        },
        VisibleWhen = Describe(field.VisibleWhen),
        RequiredWhen = Describe(field.RequiredWhen),
        Relation = field.Relation is { } relation
            ? new FieldRelationDescription { Target = relation.Target, Reverse = relation.Reverse, Ordered = relation.Ordered }
            : null,
        Settings = field.Settings,
    };

    private static ConditionDescription? Describe(Condition? condition) => condition switch
    {
        null => null,
        { Operator: ConditionOperator.And or ConditionOperator.Or or ConditionOperator.Not } => new ConditionDescription
        {
            Operator = condition.Operator,
            Operands = [.. condition.Operands.Select(operand => Describe(operand)!)],
        },
        _ => new ConditionDescription
        {
            Operator = condition.Operator,
            Field = condition.FieldName,
            Values = condition.Operator == ConditionOperator.IsEmpty ? null : condition.Values,
        },
    };
}

using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

// A plugin type whose setting "from" names a sibling field, as a slug's source does.
public sealed class ReferenceFieldType : IFieldType
{
    public const string TypeName = "excerpt";
    public const string From = "from";

    public static FieldTypeRegistry Registry { get; } = new([new ReferenceFieldType()]);

    public string Name => TypeName;

    public IReadOnlyList<Type> ClrTypes { get; } = [typeof(string)];

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator>();

    public IReadOnlyList<FieldSettingDescriptor> Settings { get; } = [new FieldSettingDescriptor { Key = From, Kind = FieldSettingKind.FieldReference }];

    public static SchemaBuilder Builder() => new(Registry);

    public bool Sortable(SchemaField field) => false;

    public bool Indexable(SchemaField field) => false;

    public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Column;

    public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        result = value;
        message = null;
        return true;
    }

    public JsonObject OpenApiSchema(SchemaField field) => new() { ["type"] = "string" };
}

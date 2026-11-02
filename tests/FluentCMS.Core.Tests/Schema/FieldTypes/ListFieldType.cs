using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

// A plugin type that stores a list as JSON when the field is marked Multiple.
public sealed class ListFieldType : IFieldType
{
    public string Name => "list";

    public IReadOnlyList<Type> ClrTypes { get; } = [typeof(string)];

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator>();

    public bool SupportsMultiple => true;

    public bool Sortable(SchemaField field) => false;

    public bool Indexable(SchemaField field) => false;

    public IReadOnlyList<FieldSettingDescriptor> Settings { get; } = [];

    public FieldStorageKind StorageKind(SchemaField field) => field.Multiple ? FieldStorageKind.Json : FieldStorageKind.Column;

    public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        result = value;
        message = null;
        return true;
    }

    public JsonObject OpenApiSchema(SchemaField field) => [];
}

using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

// A plugin type that keeps free-form options, so it accepts any setting.
public sealed class FreeFormFieldType : IFieldType
{
    public string Name => "freeform";

    public IReadOnlyList<Type> ClrTypes { get; } = [typeof(string)];

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator>();

    public bool Sortable(SchemaField field) => false;

    public bool Indexable(SchemaField field) => false;

    public IReadOnlyList<FieldSettingDescriptor> Settings { get; } = [];

    public bool AcceptsAnySetting => true;

    public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Column;

    public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        result = value;
        message = null;
        return true;
    }

    public JsonObject OpenApiSchema(SchemaField field) => [];
}

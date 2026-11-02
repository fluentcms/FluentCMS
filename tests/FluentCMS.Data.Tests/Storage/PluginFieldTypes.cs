using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Data.Tests.Storage;

// Plugin field types that the storage mapping knows nothing about; only the storage kind tells it where they live.
internal abstract class PluginFieldType(string name, FieldStorageKind kind) : IFieldType
{
    public string Name => name;

    public IReadOnlyList<Type> ClrTypes { get; } = [typeof(string)];

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator>();

    public IReadOnlyList<FieldSettingDescriptor> Settings { get; } = [];

    public bool SupportsMultiple => kind == FieldStorageKind.Json;

    public bool Sortable(SchemaField field) => false;

    public bool Indexable(SchemaField field) => true;

    public FieldStorageKind StorageKind(SchemaField field) => kind;

    public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        result = value;
        message = null;
        return true;
    }

    public JsonObject OpenApiSchema(SchemaField field) => [];
}

internal sealed class ColorFieldType() : PluginFieldType("color", FieldStorageKind.Column);

internal sealed class PaletteFieldType() : PluginFieldType("palette", FieldStorageKind.Json);

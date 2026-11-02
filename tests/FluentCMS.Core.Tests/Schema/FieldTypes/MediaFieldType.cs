using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Media;
using FluentCMS.Schema;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

// Stands in for the descriptor the media plugin registers: stored as references, filtered by id.
public sealed class MediaFieldType : IFieldType
{
    public string Name => "media";

    public IReadOnlyList<Type> ClrTypes { get; } = [typeof(MediaRef), typeof(MediaRefList)];

    public IReadOnlySet<FilterOperator> FilterOperators { get; } = new HashSet<FilterOperator> { FilterOperator.Equal, FilterOperator.In };

    public bool SupportsMultiple => true;

    public bool Sortable(SchemaField field) => false;

    public bool Indexable(SchemaField field) => false;

    public IReadOnlyList<FieldSettingDescriptor> Settings { get; } = [];

    public FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Reference;

    public bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        result = null;
        message = null;
        switch (value)
        {
            case null:
                return true;
            case Guid id:
                result = new MediaRef(id);
                return true;
            case string text when Guid.TryParse(text, out var parsed):
                result = new MediaRef(parsed);
                return true;
            default:
                message = "is not a media id";
                return false;
        }
    }

    public JsonObject OpenApiSchema(SchemaField field) => new() { ["type"] = "string", ["format"] = "uuid" };
}

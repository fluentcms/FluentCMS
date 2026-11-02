using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

// A single relation coerces to a Guid, or to null for an empty reference; a to-many relation to a Guid[] in order.
internal sealed class RelationFieldType()
    : CoreFieldType(
        FieldTypeNames.Relation,
        [typeof(Ref<>), typeof(RefList<>)],
        [FilterOperator.Equal, FilterOperator.In, .. FilterSets.NullChecks],
        sortable: false,
        indexable: false)
{
    public override FieldStorageKind StorageKind(SchemaField field) => FieldStorageKind.Reference;

    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message)
    {
        var raw = RawValue.Unwrap(value);
        if (raw is null)
        {
            return Ok(null, out result, out message);
        }

        if (!field.Multiple)
        {
            if (!TryReadId(raw, out var id))
            {
                return Fail("is not an entry id: expected a GUID", out result, out message);
            }

            return Ok(id == Guid.Empty ? null : id, out result, out message);
        }

        IEnumerable<object?> items;
        switch (raw)
        {
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                items = array.EnumerateArray().Select(item => RawValue.Unwrap(item));
                break;
            case string or JsonElement:
                return Expected("a list of entry ids", raw, out result, out message);
            case IEnumerable list:
                items = list.Cast<object?>();
                break;
            default:
                return Expected("a list of entry ids", raw, out result, out message);
        }

        List<Guid> ids = [];
        foreach (var item in items)
        {
            if (!TryReadId(item, out var id) || id == Guid.Empty)
            {
                return Fail($"item {ids.Count} is not an entry id: expected a GUID", out result, out message);
            }

            ids.Add(id);
        }

        return Ok(ids.ToArray(), out result, out message);
    }

    public override JsonObject OpenApiSchema(SchemaField field)
    {
        var id = new JsonObject { ["type"] = "string", ["format"] = "uuid" };
        return field.Multiple ? new JsonObject { ["type"] = "array", ["items"] = id } : id;
    }

    private static bool TryReadId(object? raw, out Guid id)
    {
        switch (raw)
        {
            case Guid guid:
                id = guid;
                return true;
            case string text:
                return Guid.TryParseExact(text, "D", out id);
            case not null when raw.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(Ref<>):
                id = (Guid)type.GetProperty(nameof(Ref<ContentItem>.Id))!.GetValue(raw)!;
                return true;
            default:
                id = Guid.Empty;
                return false;
        }
    }
}

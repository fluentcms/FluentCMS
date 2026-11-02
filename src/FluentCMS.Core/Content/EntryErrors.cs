using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Which validation errors a principal may hear of. An error names a field and tells something of its value, so one on a
// field it cannot read is only for it if it wrote that value itself. If nobody changed the value, the error is not about
// this write and does not block it. If a hook did, the write fails, without saying on what.
internal sealed class EntryErrors(SchemaModel schema, SchemaType type, EffectivePermissions permissions, AccessTarget target)
{
    private const string Discriminator = "type";

    private static readonly ValidationError ProducedValue = new("", ValidationErrorCodes.InvalidValue, "A value set while saving the entry is not valid.");

    public List<ValidationError> Reportable(IReadOnlyList<ValidationError> errors, JsonObject now, JsonObject? was, JsonObject sent)
    {
        List<ValidationError> result = [];
        foreach (var error in errors)
        {
            if (permissions.IsSystem || Describe(error.Path, now) is not { } path || path.Readable)
            {
                result.Add(error);
                continue;
            }

            var value = ValueAt(now, error.Path);
            if (was is not null && JsonNode.DeepEquals(value, ValueAt(was, error.Path)))
            {
                continue;
            }

            var fromClient = JsonNode.DeepEquals(value, ValueAt(sent, error.Path));
            if (fromClient && !path.Hidden)
            {
                result.Add(error);
            }
            else if (!fromClient && !result.Contains(ProducedValue))
            {
                result.Add(ProducedValue);
            }
        }

        return result;
    }

    // Null when the path is not a field, such as an error on the entry as a whole.
    private (bool Readable, bool Hidden)? Describe(string path, JsonObject now)
    {
        var segments = Parse(path);
        if (segments.Count == 0 || type.FindField(segments[0].Name) is not { } top)
        {
            return null;
        }

        var hidden = false;
        SchemaType? owner = type;
        JsonNode? value = now;
        foreach (var (name, index) in segments)
        {
            var field = owner?.FindField(name);
            hidden |= field?.Hidden == true;
            value = value is JsonObject container ? container[name] : null;
            if (index is { } position)
            {
                value = value is JsonArray items && position < items.Count ? items[position] : null;
            }

            owner = field?.FieldType switch
            {
                FieldTypeNames.Component => schema.FindComponent((string)field.Settings[FieldSettings.Component]),
                FieldTypeNames.Blocks when value is JsonObject block && block[Discriminator] is JsonValue kind && kind.TryGetValue<string>(out var blockName) => schema.FindBlock(blockName),
                _ => null,
            };
        }

        return (!hidden && permissions.CanReadField(type, top.Name, target), hidden);
    }

    private static JsonNode? ValueAt(JsonObject root, string path)
    {
        JsonNode? value = root;
        foreach (var (name, index) in Parse(path))
        {
            value = value is JsonObject container ? container[name] : null;
            if (index is { } position)
            {
                value = value is JsonArray items && position < items.Count ? items[position] : null;
            }
        }

        return value;
    }

    // sections[2].heading is (sections, 2), (heading, null).
    private static List<(string Name, int? Index)> Parse(string path)
    {
        List<(string Name, int? Index)> segments = [];
        if (path.Length == 0)
        {
            return segments;
        }

        foreach (var part in path.Split('.'))
        {
            var open = part.IndexOf('[', StringComparison.Ordinal);
            segments.Add(open < 0
                ? (part, null)
                : (part[..open], int.TryParse(part.AsSpan(open + 1, part.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null));
        }

        return segments;
    }
}

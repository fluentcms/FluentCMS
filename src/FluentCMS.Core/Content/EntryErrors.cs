using System.Globalization;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content;

// Which validation errors a principal may hear of. An error names a field and tells something of its value, so one on a
// field it cannot read is only for it if it sent that value itself. Otherwise a hook or a rule across fields produced it:
// a hook's value fails the write without saying on what. For the rest the stored entry decides: the same error there is
// not about this write and does not block it, and an error that is new fails the write in the same way. A value the
// client sent is always reported, or a refusal would say whether it equals the stored one.
internal sealed class EntryErrors(SchemaModel schema, SchemaType type, EffectivePermissions permissions, AccessTarget target)
{
    private const string Discriminator = "type";
    private const string InlineData = "data";
    private const string InlineBlockType = "blockType";

    public static readonly ValidationError ProducedValue = new("", ValidationErrorCodes.InvalidValue, "A value set while saving the entry is not valid.");

    public static readonly ValidationError UnreadableValue = new("", ValidationErrorCodes.InvalidValue, "The entry cannot be published: a value the principal may not read is not valid.");

    // sent is the entry before any hook, and fromClient only what the client itself sent: a patch into a component names
    // some of its fields, and the rest of them are stored values it did not send.
    public (List<ValidationError> Reportable, List<ValidationError> ToCompare, bool Produced) Sort(IReadOnlyList<ValidationError> errors, JsonObject now, JsonObject sent, JsonObject fromClient)
    {
        List<ValidationError> reportable = [];
        List<ValidationError> toCompare = [];
        var produced = false;
        foreach (var error in errors)
        {
            if (permissions.IsSystem || Describe(error.Path, now) is not { } path || path.Readable)
            {
                reportable.Add(error);
                continue;
            }

            var same = JsonNode.DeepEquals(ValueAt(now, error.Path), ValueAt(sent, error.Path));
            if (same && !path.Hidden && WasSent(fromClient, error.Path))
            {
                reportable.Add(error);
            }
            else if (same)
            {
                toCompare.Add(error);
            }
            else
            {
                produced = true;
            }
        }

        return (reportable, toCompare, produced);
    }

    // On publish nothing was sent and every error blocks. One on a field the principal cannot read says nothing of that
    // field, not even which one, since a rule's message can quote the value.
    public List<ValidationError> ForPublish(IReadOnlyList<ValidationError> errors, JsonObject now)
    {
        List<ValidationError> reportable = [];
        var unreadable = false;
        foreach (var error in errors)
        {
            if (permissions.IsSystem || Describe(error.Path, now) is not { } path || path.Readable)
            {
                reportable.Add(error);
            }
            else
            {
                unreadable = true;
            }
        }

        if (unreadable)
        {
            reportable.Add(UnreadableValue);
        }

        return reportable;
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
        var inDocument = false;
        SchemaType? owner = type;
        JsonNode? value = now;
        foreach (var (name, index) in segments)
        {
            var parent = value;
            value = value is JsonObject container ? container[name] : null;
            if (index is { } position)
            {
                value = value is JsonArray items && position < items.Count ? items[position] : null;
            }

            // Through the nodes of a rich text document to the data of an inline block, whose fields are its block's.
            if (inDocument)
            {
                if (name == InlineData && parent is JsonObject attributes && attributes[InlineBlockType] is JsonValue inline && inline.TryGetValue<string>(out var inlineName))
                {
                    owner = schema.FindBlock(inlineName);
                    inDocument = false;
                }

                continue;
            }

            var field = owner?.FindField(name);
            hidden |= field?.Hidden == true;
            inDocument = field?.FieldType == FieldTypeNames.RichText;
            owner = field?.FieldType switch
            {
                FieldTypeNames.Component => schema.FindComponent((string)field.Settings[FieldSettings.Component]),
                FieldTypeNames.Blocks when value is JsonObject block && block[Discriminator] is JsonValue kind && kind.TryGetValue<string>(out var blockName) => schema.FindBlock(blockName),
                _ => null,
            };
        }

        return (!hidden && permissions.CanReadField(type, top.Name, target), hidden);
    }

    private static bool WasSent(JsonObject fromClient, string path)
    {
        JsonNode? value = fromClient;
        foreach (var (name, index) in Parse(path))
        {
            if (AsJson(value) is not JsonObject container || !container.ContainsKey(name))
            {
                return false;
            }

            value = container[name];
            if (index is { } position)
            {
                if (AsJson(value) is not JsonArray items || position >= items.Count)
                {
                    return false;
                }

                value = items[position];
            }
        }

        return true;
    }

    // A component or block may arrive as JSON text.
    private static JsonNode? AsJson(JsonNode? value)
    {
        if (value is not JsonValue text || !text.TryGetValue<string>(out var json))
        {
            return value;
        }

        return RawValue.TryReadJson(json, out var parsed, out _) ? parsed : value;
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

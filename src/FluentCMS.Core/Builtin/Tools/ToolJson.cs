using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Builtin.Tools;

// How the built-in tools show the model to an agent. Names of states and kinds are snake_case, as the audit log's operations are.
internal static class ToolJson
{
    public static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
    }

    public static JsonObject Summary(ContentTypeSummary type)
    {
        return new JsonObject
        {
            ["id"] = type.Id.ToString(),
            ["slug"] = type.Slug,
            ["name"] = type.Name,
            ["version"] = type.Version,
        };
    }

    public static JsonObject ContentType(ContentType type)
    {
        var fields = new JsonArray();
        foreach (var field in type.Definition.Fields)
        {
            fields.Add(new JsonObject
            {
                ["name"] = field.Name,
                ["type"] = field.Type,
                ["required"] = field.Required,
                ["description"] = field.Description,
                ["settings"] = Node(field.Settings),
            });
        }

        return new JsonObject
        {
            ["id"] = type.Id.ToString(),
            ["slug"] = type.Slug,
            ["name"] = type.Name,
            ["version"] = type.Version,
            ["entity"] = type.Definition.IsEntity,
            ["description"] = type.Definition.Description,
            ["fields"] = fields,
        };
    }

    public static JsonObject ChangeSet(ChangeSet set)
    {
        var (kind, name) = set.Initiator switch
        {
            AgentInitiator agent => ("agent", agent.DisplayName),
            _ => ("user", set.Initiator.DisplayName),
        };

        return new JsonObject
        {
            ["id"] = set.Id.ToString(),
            ["state"] = Name(set.State),
            ["rationale"] = set.Rationale,
            ["sources"] = new JsonArray([.. set.Sources.Select(source => JsonValue.Create(source))]),
            ["sessionId"] = set.SessionId,
            ["initiator"] = new JsonObject { ["kind"] = kind, ["name"] = name },
            ["rejectionReason"] = set.RejectionReason,
            ["operations"] = new JsonArray([.. set.Operations.Select(Operation)]),
        };
    }

    public static JsonObject Diff(ChangeSetDiff diff)
    {
        var items = new JsonArray();
        foreach (var item in diff.Items)
        {
            var json = Operation(item.Operation);
            json["properties"] = Values(item.Properties);
            json["fields"] = Values(item.Fields);
            items.Add(json);
        }

        return new JsonObject { ["changeSetId"] = diff.ChangeSetId.ToString(), ["items"] = items };
    }

    private static JsonObject Operation(ChangeOperation operation)
    {
        return new JsonObject
        {
            ["kind"] = Name(operation.Kind),
            ["itemKind"] = Name(operation.ItemKind),
            ["itemId"] = operation.ItemId.ToString(),
            ["baseVersion"] = operation.BaseVersion,
            ["proposedVersion"] = operation.ProposedVersion,
        };
    }

    private static JsonArray Values(IReadOnlyList<ValueDiff> values)
    {
        return new JsonArray([.. values.Select(value => new JsonObject
        {
            ["name"] = value.Name,
            ["change"] = Name(value.Change),
            ["before"] = Node(value.Before),
            ["after"] = Node(value.After),
        })]);
    }

    private static JsonNode? Node(JsonElement? value)
    {
        return value is { } element ? JsonNode.Parse(element.GetRawText()) : null;
    }
}

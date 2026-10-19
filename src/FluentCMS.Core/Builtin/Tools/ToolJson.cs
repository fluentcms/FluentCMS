using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
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

    public static JsonObject EntrySummary(EntrySummary entry)
    {
        return new JsonObject
        {
            ["id"] = entry.Id.ToString(),
            ["state"] = Name(entry.State),
            ["latestVersion"] = entry.LatestVersion,
            ["publishedVersion"] = entry.PublishedVersion,
        };
    }

    // The entry with the fields of the version given, its latest draft unless another is named.
    public static JsonObject Entry(Entry entry, EntryVersion? version = null)
    {
        var shown = version ?? entry.Latest;

        return new JsonObject
        {
            ["id"] = entry.Id.ToString(),
            ["contentType"] = entry.ContentType,
            ["state"] = Name(entry.State),
            ["latestVersion"] = entry.LatestVersion,
            ["publishedVersion"] = entry.PublishedVersion,
            ["version"] = shown.Version,
            ["contentTypeVersion"] = shown.ContentTypeVersion,
            ["fields"] = Node(shown.Fields),
        };
    }

    public static JsonObject EntryVersion(EntryVersionSummary version)
    {
        return new JsonObject
        {
            ["version"] = version.Version,
            ["contentTypeVersion"] = version.ContentTypeVersion,
            ["createdAt"] = version.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public static JsonObject Resolved(ResolvedEntry resolved)
    {
        return new JsonObject
        {
            ["entryId"] = resolved.EntryId.ToString(),
            ["contentType"] = resolved.ContentType,
            ["version"] = resolved.Version,
            ["fields"] = Node(resolved.Fields),
            ["unresolved"] = new JsonArray([.. resolved.Unresolved.Select(reference => new JsonObject
            {
                ["path"] = reference.Path,
                ["entityId"] = reference.EntityId.ToString(),
                ["field"] = reference.Field,
                ["reason"] = Name(reference.Reason),
            })]),
        };
    }

    public static JsonObject Usage(EntryUsage usage)
    {
        return new JsonObject
        {
            ["entryId"] = usage.EntryId.ToString(),
            ["contentType"] = usage.ContentType,
            ["version"] = usage.Version,
            ["isLatest"] = usage.IsLatest,
            ["isPublished"] = usage.IsPublished,
            ["paths"] = new JsonArray([.. usage.Paths.Select(path => JsonValue.Create(path))]),
        };
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

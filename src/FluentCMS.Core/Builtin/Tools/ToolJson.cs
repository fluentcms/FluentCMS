using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Media;
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

    public static JsonObject CheckResult(CheckResult result)
    {
        return new JsonObject
        {
            ["check"] = result.Check,
            ["version"] = result.Version,
            ["type"] = result.Type,
            ["kind"] = Name(result.Kind),
            ["severity"] = Name(result.Severity),
            ["description"] = result.Description,
            ["status"] = Name(result.Status),
            ["findings"] = new JsonArray([.. result.Findings.Select(finding => new JsonObject
            {
                ["message"] = finding.Message,
                ["pointer"] = finding.Pointer,
                ["excerpt"] = finding.Excerpt,
            })]),
            ["model"] = result.Model is { } model
                ? new JsonObject { ["callId"] = model.CallId.ToString(), ["provider"] = model.Provider, ["model"] = model.Model, ["cost"] = model.Cost }
                : null,
        };
    }

    public static JsonObject CheckRun(ChangeSetCheckRun run)
    {
        return new JsonObject
        {
            ["number"] = run.Number,
            ["mode"] = Name(run.Mode),
            ["status"] = Name(run.Status),
            ["cost"] = run.Cost,
            ["startedAt"] = run.StartedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["finishedAt"] = run.FinishedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["items"] = new JsonArray([.. run.Items.Select(item => new JsonObject
            {
                ["itemKind"] = Name(item.ItemKind),
                ["itemId"] = item.ItemId.ToString(),
                ["version"] = item.Version,
                ["contentType"] = item.ContentType,
                ["results"] = new JsonArray([.. item.Results.Select(CheckResult)]),
            })]),
            ["problems"] = new JsonArray([.. run.Problems.Select(problem => JsonValue.Create(problem))]),
        };
    }

    // Not the error a worker recorded, which can quote database or provider details; a failed run says what to do in its problems.
    public static JsonObject Job(Job job)
    {
        return new JsonObject
        {
            ["id"] = job.Id.ToString(),
            ["state"] = Name(job.State),
            ["done"] = job.Progress.Done,
            ["total"] = job.Progress.Total,
            ["message"] = job.Progress.Message,
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

    public static JsonObject Facts(EntityFactSheet sheet)
    {
        return new JsonObject
        {
            ["entityId"] = sheet.EntityId.ToString(),
            ["type"] = sheet.Type,
            ["version"] = sheet.Version,
            ["facts"] = new JsonArray([.. sheet.Facts.Select(fact => new JsonObject
            {
                ["field"] = fact.Field,
                ["unsourced"] = fact.IsUnsourced,
                ["source"] = fact.SourceKind is null ? null : new JsonObject { ["kind"] = fact.SourceKind, ["reference"] = fact.SourceReference },
                ["owner"] = fact.Owner?.ToString(),
                ["validFrom"] = Time(fact.ValidFrom),
                ["validUntil"] = Time(fact.ValidUntil),
                ["lastVerified"] = Time(fact.LastVerified),
            })]),
        };
    }

    public static JsonObject Flag(FactFlag flag)
    {
        return new JsonObject
        {
            ["entityId"] = flag.EntityId.ToString(),
            ["type"] = flag.Type,
            ["field"] = flag.Field,
            ["version"] = flag.Version,
            ["isLatest"] = flag.IsLatest,
            ["isPublished"] = flag.IsPublished,
        };
    }

    public static JsonObject Stale(StaleFact fact)
    {
        return new JsonObject
        {
            ["entityId"] = fact.EntityId.ToString(),
            ["type"] = fact.Type,
            ["field"] = fact.Field,
            ["version"] = fact.Version,
            ["isLatest"] = fact.IsLatest,
            ["isPublished"] = fact.IsPublished,
            ["reason"] = fact.Reason == StaleReason.Expired ? "expired" : "unverified",
            ["staleSince"] = Time(fact.StaleSince),
        };
    }

    public static JsonObject Relations(EntityRelations relations)
    {
        return new JsonObject
        {
            ["entityId"] = relations.EntityId.ToString(),
            ["type"] = relations.Type,
            ["outgoing"] = new JsonArray([.. relations.Outgoing.Select(Relation)]),
            ["incoming"] = new JsonArray([.. relations.Incoming.Select(Relation)]),
        };
    }

    public static JsonObject BlastRadius(BlastRadius radius)
    {
        return new JsonObject
        {
            ["changeSetId"] = radius.ChangeSetId.ToString(),
            ["entries"] = new JsonArray([.. radius.Entries.Select(entry => new JsonObject
            {
                ["entryId"] = entry.EntryId.ToString(),
                ["contentType"] = entry.ContentType,
                ["version"] = entry.Version,
                ["fields"] = Values(entry.Fields),
            })]),
        };
    }

    public static JsonObject Media(MediaAsset asset, MediaAssetVersion? version = null)
    {
        var shown = version ?? asset.Latest;

        return new JsonObject
        {
            ["id"] = asset.Id.ToString(),
            ["latestVersion"] = asset.LatestVersion,
            ["publishedVersion"] = asset.PublishedVersion,
            ["version"] = shown.Version,
            ["contentType"] = shown.Blob.ContentType,
            ["length"] = shown.Blob.Length,
            ["sha256"] = shown.Blob.Sha256,
            ["fileName"] = shown.Metadata.FileName,
            ["folder"] = shown.Metadata.Folder,
            ["tags"] = new JsonArray([.. shown.Metadata.Tags.Select(tag => JsonValue.Create(tag))]),
            ["altText"] = shown.Metadata.AltText,
            ["caption"] = shown.Metadata.Caption,
        };
    }

    public static JsonObject MediaSummary(MediaAssetSummary asset)
    {
        return new JsonObject
        {
            ["id"] = asset.Id.ToString(),
            ["latestVersion"] = asset.LatestVersion,
            ["publishedVersion"] = asset.PublishedVersion,
            ["fileName"] = asset.FileName,
            ["contentType"] = asset.ContentType,
            ["length"] = asset.Length,
            ["folder"] = asset.Folder,
            ["tags"] = new JsonArray([.. asset.Tags.Select(tag => JsonValue.Create(tag))]),
            ["hasAltText"] = asset.HasAltText,
        };
    }

    private static JsonObject Relation(EntityRelation relation)
    {
        return new JsonObject
        {
            ["fromId"] = relation.FromId.ToString(),
            ["fromType"] = relation.FromType,
            ["field"] = relation.Field,
            ["toId"] = relation.ToId.ToString(),
            ["toType"] = relation.ToType,
            ["toField"] = relation.ToField,
            ["isLatest"] = relation.IsLatest,
            ["isPublished"] = relation.IsPublished,
        };
    }

    private static string? Time(DateTimeOffset? time)
    {
        return time?.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
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

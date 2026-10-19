using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Entries (CNT-01 to CNT-05, MCP-03): reading them as stored, resolved and in use, and proposing creates, updates and unpublishing,
// which go into a change set a person reviews. Entities are entries of entity types, so their fields are facts.
public sealed class EntryTools : IToolProvider
{
    private const string FindEntries = "Call list_entries with the content type's slug for the ids of its entries.";
    private const string ReadSchema = "Call get_schema_document with the content type's slug for its fields and the values each one takes.";

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "list_entries",
            "List entries",
            "Lists the entries of one content type, oldest first, with each one's id, state (draft, in_review, published, reverted "
            + "or archived), latest version and published version. Read one with get_entry.",
            ToolSchemas.Object(ContentTypeArgument),
            readOnly: true,
            ListAsync),
        new AgentTool(
            "get_entry",
            "Get an entry",
            "Returns one entry with its state, its latest and published versions and the fields of its latest draft, or of the "
            + "version you name. A reference field holds the id of the entry or entity it points at; get_resolved_entry shows the "
            + "values instead. Update from latestVersion.",
            ToolSchemas.Object(
                IdArgument,
                ToolSchemas.PositiveInteger("version", "A version to read instead of the latest draft.", 1, required: false)),
            readOnly: true,
            GetAsync),
        new AgentTool(
            "get_entry_history",
            "Get an entry's history",
            "Lists every version of an entry, oldest first, with the content type version it was written against and when. "
            + "Versions are never overwritten, so any of them can be read with get_entry.",
            ToolSchemas.Object(IdArgument),
            readOnly: true,
            HistoryAsync),
        new AgentTool(
            "get_resolved_entry",
            "Get an entry with its facts resolved",
            "Returns a version of an entry, the latest draft unless you name one, with every reference to an entity field replaced "
            + "by that entity's current published value, such as a product's price on a product page. A reference that cannot "
            + "be resolved holds null and is listed under unresolved with its reason.",
            ToolSchemas.Object(
                IdArgument,
                ToolSchemas.PositiveInteger("version", "A version to resolve instead of the latest draft.", 1, required: false)),
            readOnly: true,
            ResolvedAsync),
        new AgentTool(
            "get_where_used",
            "Find where something is used",
            "Lists the entries whose latest draft or published version references an entry or entity, one field of an entity "
            + "(give entry_id and field, such as a product's price), or a media asset (give asset_id), with the path of each "
            + "reference. Call it before changing a fact to see which pages show it.",
            ToolSchemas.Object(
                ToolSchemas.Uuid("entry_id", "The entry or entity to look for. Give this or asset_id.", required: false),
                ToolSchemas.String("field", "With entry_id: one field of the entity, to find only references to that field.", "price", required: false),
                ToolSchemas.Uuid("asset_id", "The media asset to look for. Give this or entry_id.", required: false)),
            readOnly: true,
            WhereUsedAsync),
        new AgentTool(
            "create_entry",
            "Propose a new entry",
            "Proposes a new entry of a content type: it stores version 1 as a draft and adds the create to a change set in the "
            + "same call. It is not published until a person approves and publishes the set. Every problem with the fields comes back "
            + "at once, by field; get_schema_document shows what each field takes. A reference field takes the id of an entry or entity.",
            ToolSchemas.Object([ContentTypeArgument, FieldsArgument, .. ChangeSetTarget.Arguments]),
            readOnly: false,
            CreateAsync),
        new AgentTool(
            "update_entry",
            "Propose an update of an entry",
            "Proposes new fields for an entry from the version you read, given as base_version: it stores the next version as a "
            + "draft and adds the update to a change set in the same call. The fields replace the whole previous version, so give "
            + "every field to keep. If someone updated the entry since you read it, the call is refused; read it again with get_entry.",
            ToolSchemas.Object([
                IdArgument,
                ToolSchemas.PositiveInteger("base_version", "The latest version you read, as get_entry returned it.", 1),
                FieldsArgument,
                .. ChangeSetTarget.Arguments]),
            readOnly: false,
            UpdateAsync),
        new AgentTool(
            "delete_entry",
            "Propose unpublishing an entry",
            "Proposes taking a published entry offline: it adds a delete to a change set, and publishing the set unpublishes the "
            + "entry and archives it. Its versions stay, so the change can be reverted. Call get_where_used first to see which "
            + "entries reference it.",
            ToolSchemas.Object([IdArgument, .. ChangeSetTarget.Arguments]),
            readOnly: false,
            DeleteAsync),
    ];

    private static ToolArgument ContentTypeArgument { get; } =
        ToolSchemas.String("content_type", "The content type's slug, as list_content_types shows it.", "product-page");

    private static ToolArgument IdArgument { get; } = ToolSchemas.Uuid("id", "The entry's id, as list_entries or create_entry returned it.");

    private static ToolArgument FieldsArgument { get; } = ToolSchemas.Json(
        "fields",
        "The entry's fields, one property per field of the content type; get_schema_document shows each field's values and an "
        + "example entry. On an entity type every value is a fact with its source, owner and validity.",
        new JsonObject { ["type"] = "object" },
        JsonNode.Parse("""{"headline": "The Widget", "price": "0199a000-0000-7000-8000-000000000001"}""")!);

    private static async Task<ToolResult> ListAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var contentType = arguments.RequiredString("content_type", "product-page");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var entries = await call.Services.GetRequiredService<IEntryDirectory>().ListAsync(contentType!, cancellationToken);
        return ToolResult.From(entries, list => new JsonObject { ["entries"] = new JsonArray([.. list.Select(ToolJson.EntrySummary)]) }, Hint(entries.Errors));
    }

    private static async Task<ToolResult> GetAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var version = arguments.OptionalPositiveInteger("version");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var directory = call.Services.GetRequiredService<IEntryDirectory>();
        var entry = await directory.GetAsync(id!.Value, cancellationToken);
        if (!entry.IsSuccess || version is not { } number)
        {
            return ToolResult.From(entry, read => ToolJson.Entry(read), Hint(entry.Errors));
        }

        var stored = await directory.GetVersionAsync(id.Value, number, cancellationToken);
        return ToolResult.From(stored, read => ToolJson.Entry(entry.Value, read), "Leave out the version to read the latest draft.");
    }

    private static async Task<ToolResult> HistoryAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var history = await call.Services.GetRequiredService<IEntryDirectory>().HistoryAsync(id!.Value, cancellationToken);
        return ToolResult.From(history, versions => new JsonObject { ["versions"] = new JsonArray([.. versions.Select(ToolJson.EntryVersion)]) }, Hint(history.Errors));
    }

    private static async Task<ToolResult> ResolvedAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var version = arguments.OptionalPositiveInteger("version");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var resolved = await call.Services.GetRequiredService<IResolvedEntries>().ResolveAsync(id!.Value, version, cancellationToken);
        return ToolResult.From(resolved, ToolJson.Resolved, Hint(resolved.Errors));
    }

    private static async Task<ToolResult> WhereUsedAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var entryId = arguments.OptionalGuid("entry_id");
        var field = arguments.OptionalString("field", "price");
        var assetId = arguments.OptionalGuid("asset_id");
        var errors = arguments.Errors().ToList();

        if (errors.Count == 0 && (entryId is null) == (assetId is null))
        {
            errors.Add(new Error(ToolErrorCodes.ArgumentMissing, "Give either 'entry_id' or 'asset_id', not both."));
        }
        else if (errors.Count == 0 && field is not null && entryId is null)
        {
            errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, "'field' names a field of the entity given as 'entry_id', so it needs 'entry_id'."));
        }

        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        var whereUsed = call.Services.GetRequiredService<IWhereUsed>();
        var usages = entryId is { } id
            ? field is null
                ? await whereUsed.ForEntryAsync(id, cancellationToken)
                : await whereUsed.ForEntityFieldAsync(id, field, cancellationToken)
            : Result<IReadOnlyList<EntryUsage>>.Success(await whereUsed.ForMediaAsync(assetId!.Value, cancellationToken));

        return ToolResult.From(usages, list => new JsonObject { ["usages"] = new JsonArray([.. list.Select(ToolJson.Usage)]) }, Hint(usages.Errors));
    }

    private static async Task<ToolResult> CreateAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var contentType = arguments.RequiredString("content_type", "product-page");
        var fields = arguments.RequiredObject("fields", FieldsForm);
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        errors.InsertRange(0, arguments.Errors());
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        return await ProposeAsync(
            call,
            target!,
            entries => entries.CreateAsync(new NewEntry(contentType!, fields!.Value), cancellationToken),
            (entry, kind) => ChangeOperation.Create(OperationKind.Create, kind, entry.Id, null, entry.LatestVersion),
            cancellationToken);
    }

    // The operation is based on what is published, which is what publishing it replaces; a draft never published is still a create.
    private static async Task<ToolResult> UpdateAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var baseVersion = arguments.RequiredPositiveInteger("base_version");
        var fields = arguments.RequiredObject("fields", FieldsForm);
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        errors.InsertRange(0, arguments.Errors());
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        return await ProposeAsync(
            call,
            target!,
            entries => entries.UpdateAsync(id!.Value, baseVersion!.Value, fields!.Value, cancellationToken),
            (entry, kind) => entry.PublishedVersion is { } published
                ? ChangeOperation.Create(OperationKind.Update, kind, entry.Id, published, entry.LatestVersion)
                : ChangeOperation.Create(OperationKind.Create, kind, entry.Id, null, entry.LatestVersion),
            cancellationToken);
    }

    private static async Task<ToolResult> DeleteAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        errors.InsertRange(0, arguments.Errors());
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        return await ProposeAsync(
            call,
            target!,
            entries => entries.GetAsync(id!.Value, cancellationToken),
            (entry, kind) => entry.PublishedVersion is { } published
                ? ChangeOperation.Create(OperationKind.Delete, kind, entry.Id, published, null)
                : Result<ChangeOperation>.Failure(
                    ContentErrorCodes.EntryNotPublished,
                    $"Entry {entry.Id} is not published, so there is nothing to take offline."),
            cancellationToken);
    }

    // The version an agent writes and its operation in the change set commit together or not at all, as for content types, so no
    // version an agent writes exists outside a change set a person reviews (AUD-N1, CHG-01).
    private static async Task<ToolResult> ProposeAsync(
        ToolInvocation call,
        ChangeSetTarget target,
        Func<IEntryDirectory, Task<Result<Entry>>> write,
        Func<Entry, ItemKind, Result<ChangeOperation>> operation,
        CancellationToken cancellationToken)
    {
        var services = call.Services;
        var ready = await target.CheckAsync(services, cancellationToken);
        if (!ready.IsSuccess)
        {
            return ToolResult.Failure(ready.Errors);
        }

        // Each directory finds its expected failures before its own unit of work, so leaving this one uncommitted undoes them all.
        await using var scope = await services.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);

        var entry = await write(services.GetRequiredService<IEntryDirectory>());
        if (!entry.IsSuccess)
        {
            return ToolResult.Failure(entry.Errors, Hint(entry.Errors));
        }

        // Types are never deleted, so the type of a stored entry is always there.
        var type = await services.GetRequiredService<IContentTypeDirectory>().GetAsync(entry.Value.ContentType, cancellationToken);
        var kind = type.Value.Definition.IsEntity ? ItemKind.Entity : ItemKind.Entry;
        var proposed = operation(entry.Value, kind);
        if (!proposed.IsSuccess)
        {
            return ToolResult.Failure(proposed.Errors, Hint(proposed.Errors));
        }

        var set = await target.AddAsync(services.GetRequiredService<IChangeSetDirectory>(), proposed.Value, cancellationToken);
        if (!set.IsSuccess)
        {
            return ToolResult.Failure(set.Errors);
        }

        await scope.CommitAsync(cancellationToken);

        return ToolResult.Success(new JsonObject
        {
            ["entry"] = ToolJson.Entry(entry.Value),
            ["changeSet"] = ToolJson.ChangeSet(set.Value),
            ["next"] = $"The {ToolJson.Name(proposed.Value.Kind)} is in change set {set.Value.Id}, which stays open for more "
                + "operations. Call submit_change_set when it is complete; checks run and a person reviews it before it is published.",
        });
    }

    private const string FieldsForm = "an object with one property per field, such as {\"headline\": \"The Widget\"}";

    private static string? Hint(IReadOnlyList<Error> errors)
    {
        return errors.FirstOrDefault()?.Code switch
        {
            ContentErrorCodes.VersionConflict => "Call get_entry for the latest version and its fields, then update from that version.",
            ContentErrorCodes.EntryNotFound => FindEntries,
            ContentErrorCodes.VersionNotFound => "Call get_entry_history for the versions the entry has.",
            ContentErrorCodes.FieldsMalformed or ContentErrorCodes.FieldUnknown or ContentErrorCodes.FieldRequired or SchemaErrorCodes.ValueInvalid => ReadSchema,
            ContentErrorCodes.ReferenceNotFound or ContentErrorCodes.ReferenceTypeMismatch =>
                "Call list_entries with the referenced content type's slug for the ids a reference field can take.",
            ContentErrorCodes.EntryNotPublished => "Only a published entry can be taken offline; get_entry shows its publishedVersion.",
            SchemaErrorCodes.ContentTypeNotFound => "Call list_content_types for the slugs of the site's content types.",
            _ => null,
        };
    }
}

using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// An entry or entity write an agent proposes, shared by the entry and entity tools.
internal static class ProposedEntryWrite
{
    private const string FindEntries = "Call list_entries with the content type's slug for the ids of its entries.";
    private const string ReadSchema = "Call get_schema_document with the content type's slug for its fields and the values each one takes.";

    // The version an agent writes and its operation in the change set commit together or not at all, as for content types, so no
    // version an agent writes exists outside a change set a person reviews (AUD-N1, CHG-01).
    public static async Task<ToolResult> RunAsync(
        ToolInvocation call,
        ChangeSetTarget target,
        Func<IEntryDirectory, Task<Result<Entry>>> write,
        Func<Entry, ItemKind, Result<ChangeOperation>> operation,
        CancellationToken cancellationToken,
        Guid? itemId = null)
    {
        var services = call.Services;
        var ready = await target.CheckAsync(services, itemId, cancellationToken);
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

        // Types are never deleted, but reading one is scope-checked, so an agent that may write a type's entries without reading it is refused here.
        var type = await services.GetRequiredService<IContentTypeDirectory>().GetAsync(entry.Value.ContentType, cancellationToken);
        if (!type.IsSuccess)
        {
            return ToolResult.Failure(type.Errors);
        }

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

    // An update is based on what is published, which is what publishing it replaces; a draft never published is still a create.
    public static Result<ChangeOperation> Update(Entry entry, ItemKind kind)
    {
        return entry.PublishedVersion is { } published
            ? ChangeOperation.Create(OperationKind.Update, kind, entry.Id, published, entry.LatestVersion)
            : ChangeOperation.Create(OperationKind.Create, kind, entry.Id, null, entry.LatestVersion);
    }

    public static string? Hint(IReadOnlyList<Error> errors)
    {
        return errors.FirstOrDefault()?.Code switch
        {
            ContentErrorCodes.VersionConflict => "Call get_entry for the latest version and its fields, then update from that version.",
            ContentErrorCodes.EntryNotFound => FindEntries,
            ContentErrorCodes.VersionNotFound => "Call get_entry_history for the versions the entry has.",
            ContentErrorCodes.FieldsMalformed or ContentErrorCodes.FieldUnknown or ContentErrorCodes.FieldRequired or SchemaErrorCodes.ValueInvalid => ReadSchema,
            ContentErrorCodes.ReferenceNotFound or ContentErrorCodes.ReferenceTypeMismatch =>
                "Call list_entries with the referenced content type's slug for the ids a reference field can take; a media field takes the id of an uploaded asset.",
            ContentErrorCodes.EntryNotPublished => "Only a published entry can be taken offline; get_entry shows its publishedVersion.",
            EntityErrorCodes.FieldNotRelation => "Call get_content_type for the entity type's reference fields and the types they point at.",
            ContentErrorCodes.EntryNotEntity => "Only entities have facts and relations; get_content_type shows whether a type is an entity type.",
            SchemaErrorCodes.ContentTypeNotFound => "Call list_content_types for the slugs of the site's content types.",
            _ => null,
        };
    }
}

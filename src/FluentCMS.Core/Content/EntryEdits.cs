using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Content;

// The directories find their expected failures before their own units of work, so leaving this one uncommitted undoes them all:
// no version a form writes exists outside the change set that proposes it (AUD-N1, CHG-01).
internal sealed class EntryEdits(
    IUnitOfWork unitOfWork,
    IEntryDirectory entries,
    IContentTypeDirectory contentTypes,
    IChangeSetDirectory changeSets) : IEntryEdits
{
    public async Task<Result<SavedEntryEdit>> SaveAsync(EntryEdit edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        if (edit.EntryId is not null && edit.BaseVersion is null)
        {
            return Result<SavedEntryEdit>.Failure(ContentErrorCodes.VersionConflict, "An update names the version it was made from.");
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var entry = edit.EntryId is { } id
            ? await entries.UpdateAsync(id, edit.BaseVersion!.Value, edit.Fields, cancellationToken)
            : await entries.CreateAsync(new NewEntry(edit.ContentType, edit.Fields), cancellationToken);
        if (!entry.IsSuccess)
        {
            return Result<SavedEntryEdit>.Failure(entry.Errors);
        }

        var type = await contentTypes.GetAsync(entry.Value.ContentType, cancellationToken);
        if (!type.IsSuccess)
        {
            return Result<SavedEntryEdit>.Failure(type.Errors);
        }

        // An update is based on what is published, which publishing it replaces; a draft never published is still a create.
        var kind = type.Value.Definition.IsEntity ? ItemKind.Entity : ItemKind.Entry;
        var operation = entry.Value.PublishedVersion is { } published
            ? ChangeOperation.Create(OperationKind.Update, kind, entry.Value.Id, published, entry.Value.LatestVersion)
            : ChangeOperation.Create(OperationKind.Create, kind, entry.Value.Id, null, entry.Value.LatestVersion);
        if (!operation.IsSuccess)
        {
            return Result<SavedEntryEdit>.Failure(operation.Errors);
        }

        var opened = await changeSets.OpenAsync(edit.ChangeSet, cancellationToken);
        if (!opened.IsSuccess)
        {
            return Result<SavedEntryEdit>.Failure(opened.Errors);
        }

        var set = await changeSets.AddOperationAsync(opened.Value.Id, operation.Value, cancellationToken);
        if (!set.IsSuccess)
        {
            return Result<SavedEntryEdit>.Failure(set.Errors);
        }

        await scope.CommitAsync(cancellationToken);
        return Result<SavedEntryEdit>.Success(new SavedEntryEdit(entry.Value, set.Value));
    }
}

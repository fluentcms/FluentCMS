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

        // Publishing that moved on since the values were read is found before anything is written, as the directories find theirs.
        if (edit.PublishedBase is { } expected && edit.EntryId is { } entryId
            && await entries.GetAsync(entryId, cancellationToken) is { IsSuccess: true } current && current.Value.PublishedVersion != expected)
        {
            return PublishedMoved(expected, current.Value.PublishedVersion);
        }

        // The set's own expected failures are found before anything is written, as the directories find theirs.
        if (string.IsNullOrWhiteSpace(edit.ChangeSet.Rationale))
        {
            return Result<SavedEntryEdit>.Failure(ChangeSetErrorCodes.RationaleMissing, "A change set needs a rationale that says why the changes are made.");
        }

        if (edit.ChangeSet.Sources.Any(string.IsNullOrWhiteSpace))
        {
            return Result<SavedEntryEdit>.Failure(ChangeSetErrorCodes.SourceBlank, "A source is blank. A source names where a change came from, such as a URL, a file, an export or a person.");
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var entry = edit.EntryId is { } id
            ? await entries.UpdateAsync(id, edit.BaseVersion!.Value, edit.Fields, cancellationToken)
            : await entries.CreateAsync(new NewEntry(edit.ContentType, edit.Fields), cancellationToken);
        if (!entry.IsSuccess)
        {
            return Result<SavedEntryEdit>.Failure(entry.Errors);
        }

        // Checked again against the published version the operation is based on below, for a publish since the check above.
        if (edit.PublishedBase is { } publishedBase && entry.Value.PublishedVersion != publishedBase)
        {
            return PublishedMoved(publishedBase, entry.Value.PublishedVersion);
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

    private static Result<SavedEntryEdit> PublishedMoved(int expected, int? published)
    {
        return Result<SavedEntryEdit>.Failure(
            ContentErrorCodes.VersionConflict,
            $"The values came from published version {expected}, and {(published is { } version ? $"version {version}" : "no version")} is published now.");
    }
}

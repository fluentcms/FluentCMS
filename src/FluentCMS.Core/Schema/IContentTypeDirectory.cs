using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Schema;

// The stored content types of the site (SCH-01, SCH-07). Every write stores a new version and records it as an operation of
// the change set it names, together with their audit events in one unit of work (SCH-05, AUD-N1). A write never changes what
// is live: only publishing its change set moves the published version.
public interface IContentTypeDirectory
{
    // The latest version of every type, with the version that is live.
    Task<IReadOnlyList<ContentTypeSummary>> ListAsync(CancellationToken cancellationToken = default);

    // The latest version, which authoring builds on: entries are checked against it and the schema document describes it.
    Task<Result<ContentType>> GetAsync(string slug, CancellationToken cancellationToken = default);

    // The live version, which delivery reads.
    Task<Result<ContentType>> GetPublishedAsync(string slug, CancellationToken cancellationToken = default);

    // Earlier versions stay readable after a revision.
    Task<Result<ContentType>> GetVersionAsync(string slug, int version, CancellationToken cancellationToken = default);

    Task<Result<ContentType>> CreateAsync(NewContentType contentType, ChangeSetTarget changeSet, CancellationToken cancellationToken = default);

    // The caller names the version it read, which is the latest one or the published one, so a revision never silently replaces
    // a version it has not seen and can leave an unpublished draft behind.
    Task<Result<ContentType>> ReviseAsync(
        string slug,
        int baseVersion,
        ContentTypeDefinition definition,
        ChangeSetTarget changeSet,
        CancellationToken cancellationToken = default);

    // As CreateAsync and ReviseAsync, with the change set the write joined, so a caller such as a tool answers from that set instead of
    // looking it up again by session, which a submit or the session's next set could change in between.
    Task<Result<ContentTypeWrite>> CreateInChangeSetAsync(NewContentType contentType, ChangeSetTarget changeSet, CancellationToken cancellationToken = default);

    Task<Result<ContentTypeWrite>> ReviseInChangeSetAsync(
        string slug,
        int baseVersion,
        ContentTypeDefinition definition,
        ChangeSetTarget changeSet,
        CancellationToken cancellationToken = default);
}

public sealed record ContentTypeWrite(ContentType ContentType, Guid ChangeSetId);

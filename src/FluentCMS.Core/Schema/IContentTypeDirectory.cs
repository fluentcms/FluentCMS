using FluentCMS.Core.Results;

namespace FluentCMS.Core.Schema;

// The stored content types of the site (SCH-01, SCH-07). Every write appends its audit event in the same unit of work (AUD-N1).
public interface IContentTypeDirectory
{
    Task<IReadOnlyList<ContentTypeSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<ContentType>> GetAsync(string slug, CancellationToken cancellationToken = default);

    // Earlier versions stay readable after a revision.
    Task<Result<ContentType>> GetVersionAsync(string slug, int version, CancellationToken cancellationToken = default);

    Task<Result<ContentType>> CreateAsync(NewContentType contentType, CancellationToken cancellationToken = default);

    // The caller names the version it read, so a revision never silently replaces one it has not seen.
    Task<Result<ContentType>> ReviseAsync(
        string slug,
        int baseVersion,
        ContentTypeDefinition definition,
        CancellationToken cancellationToken = default);
}

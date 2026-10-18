using FluentCMS.Core.Results;

namespace FluentCMS.Core.Content;

// Entries as resolved views (CNT-04). The references come from the reference index, never from scanning the document (ADR-0005).
// A read: it appends no audit event.
public interface IResolvedEntries
{
    // The latest draft when no version is named.
    Task<Result<ResolvedEntry>> ResolveAsync(Guid entryId, int? version = null, CancellationToken cancellationToken = default);
}

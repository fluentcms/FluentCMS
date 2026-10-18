using System.Text.Json;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Content;

// The stored entries of the site (CNT-02, SCH-07). Every write is checked against the current version of the entry's content type,
// stores a new version and appends its audit event in the same unit of work (AUD-N1); no write changes a stored version.
public interface IEntryDirectory
{
    // ContentType is a slug. Ordered by id, which follows the order of creation.
    Task<Result<IReadOnlyList<EntrySummary>>> ListAsync(string contentType, CancellationToken cancellationToken = default);

    // The entry with its latest draft.
    Task<Result<Entry>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<EntryVersion>> GetVersionAsync(Guid id, int version, CancellationToken cancellationToken = default);

    // Every version, oldest first.
    Task<Result<IReadOnlyList<EntryVersionSummary>>> HistoryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<Entry>> CreateAsync(NewEntry entry, CancellationToken cancellationToken = default);

    // The caller names the version it read, so an update never silently replaces one it has not seen.
    Task<Result<Entry>> UpdateAsync(Guid id, int baseVersion, JsonElement fields, CancellationToken cancellationToken = default);
}

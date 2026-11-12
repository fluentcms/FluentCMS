using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentCMS.Core.Storage;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Core.Content;

// One entry of a transfer package: its id and a row per locale, each with its system fields and the fields of its draft and
// published states, as EntryJson.Fields gives them, which is what a version holds. Draft is null in a package of published
// entries only, where the draft is the published state; Published is null for a locale that is not published.
internal sealed record TransferEntry(Guid Id, IReadOnlyList<TransferEntryLocale> Locales);

internal sealed record TransferEntryLocale(
    string Locale,
    EntryStatus Status,
    int DraftVersion,
    int? PublishedVersion,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? FirstPublishedAt,
    JsonObject? Draft,
    JsonObject? Published)
{
    public JsonObject DraftFields => Draft ?? Published!;
}

// The system fields an import leaves on a row of cms_document once its writes are done.
internal sealed record ImportedState(
    EntryStatus Status,
    int DraftVersion,
    int? PublishedVersion,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? FirstPublishedAt);

// The mode only the import of a transfer package sets, in the scope of one entry: the content service then lets the system
// principal write under Origin.Import, keeps the ids, times and published state of the package, publishes whatever state the
// targets of a reference are in, since the package brings them too, and in a dry run counts the targets that the
// contributors of the package bring as there.
internal sealed class TransferImportMode
{
    public bool Active { get; set; }

    public bool DryRun { get; set; }

    public IReadOnlySet<(string Target, Guid Id)> Provided { get; set; } = new HashSet<(string Target, Guid Id)>();
}

// Reads the entries of a type for an export, whole: hidden fields too, both states and every locale, outside the trash.
// Whoever calls it has checked transfer.manage; it reads what no principal's limits apply to.
internal sealed class EntryExport(EntryStore store)
{
    private const int Batch = 100;

    public async IAsyncEnumerable<TransferEntry> ReadAsync(SchemaType type, SiteId site, bool publishedOnly, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Ordered here, so that two exports of the same content are the same file whatever order the database gives.
        var ids = (await store.EntryIdsAsync(type, site, publishedOnly, cancellationToken)).Order().ToList();
        foreach (var chunk in ids.Chunk(Batch))
        {
            var rows = (await store.DocumentsOfAsync(type, chunk, cancellationToken))
                .Where(row => type.Scope == ContentScope.Global || row.SiteId == site.Value)
                .ToLookup(row => row.DocumentId);
            foreach (var id in chunk)
            {
                List<TransferEntryLocale> locales = [];
                foreach (var row in rows[id])
                {
                    if (publishedOnly && row.PublishedVersion is null)
                    {
                        continue;
                    }

                    var published = row.PublishedVersion is null ? null : EntryJson.Fields(type, await store.ReadAsync(type, row, ContentState.Published, cancellationToken));
                    var draft = publishedOnly ? null : EntryJson.Fields(type, await store.ReadAsync(type, row, ContentState.Draft, cancellationToken));
                    locales.Add(new TransferEntryLocale(
                        row.Locale,
                        publishedOnly ? EntryStatus.Published : row.Status,
                        publishedOnly ? row.PublishedVersion!.Value : row.DraftVersion,
                        row.PublishedVersion,
                        row.CreatedAt,
                        row.CreatedBy,
                        row.UpdatedAt,
                        row.UpdatedBy,
                        row.PublishedAt,
                        row.FirstPublishedAt,
                        draft,
                        published));
                }

                if (locales.Count > 0)
                {
                    yield return new TransferEntry(id, locales);
                }
            }
        }
    }
}

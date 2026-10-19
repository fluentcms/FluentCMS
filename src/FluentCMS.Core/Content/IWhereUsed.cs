using FluentCMS.Core.Results;

namespace FluentCMS.Core.Content;

// Where entries, entities, entity fields and media assets are used (CNT-05). Answers come from the reference index, never from the
// stored documents (ADR-0005), and list each entry whose latest draft or published version holds a reference.
public interface IWhereUsed
{
    // References to the entry or entity as a whole or to any of its fields.
    Task<Result<IReadOnlyList<EntryUsage>>> ForEntryAsync(Guid entryId, CancellationToken cancellationToken = default);

    // References to one field of an entity, such as product.price.
    Task<Result<IReadOnlyList<EntryUsage>>> ForEntityFieldAsync(Guid entityId, string field, CancellationToken cancellationToken = default);

    // An asset the library does not hold has no uses, so it lists none.
    Task<IReadOnlyList<EntryUsage>> ForMediaAsync(Guid assetId, CancellationToken cancellationToken = default);
}

// One version of an entry that holds references, with the JSON Pointers of those references in its fields, such as /prices/2. A
// version can be the latest draft, the published version or both.
public sealed record EntryUsage(Guid EntryId, string ContentType, int Version, bool IsLatest, bool IsPublished, IReadOnlyList<string> Paths);

using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Delivery;

// What the delivery API serves (API-01): the published version of each entry that has one and is not deleted, as a resolved view
// (CNT-04). Drafts, unpublished and deleted entries are not found, so a reader cannot tell them from ids that never existed. A read:
// it appends no audit event.
public interface IPublishedContent
{
    // The published entries of one content type, by id, which follows the order of creation.
    Task<Result<PublishedPage>> ListAsync(PublishedQuery query, CancellationToken cancellationToken = default);

    Task<Result<PublishedItem>> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

// After is the Next of the previous page. References keeps the entries whose published version points at that entry, entity or
// media asset, read from the reference index (ADR-0005).
public sealed record PublishedQuery(string ContentType, int Limit = PublishedQuery.DefaultLimit, Guid? After = null, Guid? References = null)
{
    public const int DefaultLimit = 20;

    public const int MaxLimit = 100;
}

// Next is null on the last page.
public sealed record PublishedPage(IReadOnlyList<PublishedItem> Items, Guid? Next);

public sealed record PublishedItem(Guid Id, string ContentType, int Version, JsonElement Fields, IReadOnlyList<UnresolvedReference> Unresolved);

public static class DeliveryErrorCodes
{
    public const string LimitInvalid = "limit_invalid";
    public const string ParameterInvalid = "parameter_invalid";
}

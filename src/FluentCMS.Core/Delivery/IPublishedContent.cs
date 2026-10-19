using System.Text.Json;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;

namespace FluentCMS.Core.Delivery;

// What the delivery API serves (API-01): the published version of each entry that has one and is not deleted, as a resolved view
// (CNT-04). Drafts, unpublished and deleted entries are not found, so a reader cannot tell them from ids that never existed. A read:
// it appends no audit event.
public interface IPublishedContent
{
    // The published entries of one content type, by id, which follows the order of creation.
    Task<Result<PublishedPage>> ListAsync(PublishedQuery query, CancellationToken cancellationToken = default);

    Task<Result<PublishedItem>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    // Every published entry of every type the caller may read, by type and then by id, for the site index (API-03).
    Task<IReadOnlyList<PublishedSummary>> IndexAsync(CancellationToken cancellationToken = default);
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

// Title is EntryMarkdown.Title of the published version, null when it has no text.
public sealed record PublishedSummary(Guid Id, string ContentType, string ContentTypeName, int Version, string? Title);

// The content type's name and current definition come along, since the published version is read against them.
public sealed record PublishedItem(
    Guid Id,
    string ContentType,
    string ContentTypeName,
    ContentTypeDefinition Definition,
    int Version,
    JsonElement Fields,
    IReadOnlyList<UnresolvedReference> Unresolved);

public static class DeliveryErrorCodes
{
    public const string LimitInvalid = "limit_invalid";
    public const string ParameterInvalid = "parameter_invalid";
    public const string EndpointNotFound = "endpoint_not_found";
    public const string PreviewTokenInvalid = "preview_token_invalid";
    public const string PreviewByPeopleOnly = "preview_by_people_only";
    public const string PreviewTokenNotFound = "preview_token_not_found";
    public const string ChangeSetNotPreviewable = "change_set_not_previewable";
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.OpenApi;

// The query string an operation on a content route takes beyond site and locale.
internal enum ContentParameters
{
    None,
    Single,
    List,
    Filter,
}

// A body of a content route, which the OpenAPI document writes for each content type.
internal enum ContentPayload
{
    None,
    Entry,
    List,
    EntryOrList,
    Published,
    Schedule,
    Versions,
    Diff,
    Bulk,
}

internal enum ContentPrecondition
{
    None,
    Optional,
    Required,
}

// What a route under {type} reads and answers. The OpenAPI document writes one operation for each content type from it,
// so the route's handler and its description stay side by side. EntryOrList is the Delivery list, which answers a
// singleton's one entry.
internal sealed record ContentOperation(string Name, string Summary)
{
    public ContentParameters Query { get; init; }

    public ContentPayload Request { get; init; }

    public ContentPayload Response { get; init; } = ContentPayload.Entry;

    public int Status { get; init; } = StatusCodes.Status200OK;

    public ContentPrecondition IfMatch { get; init; }

    // Whether the route takes an Idempotency-Key.
    public bool Idempotent { get; init; }

    // Whether a Management response that holds the draft gives its version as ETag.
    public bool ETag { get; init; } = true;
}

internal static class ContentOperationExtensions
{
    public static RouteHandlerBuilder Describe(this RouteHandlerBuilder builder, ContentOperation operation) => builder.WithMetadata(operation);
}

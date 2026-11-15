using Microsoft.AspNetCore.Http.Metadata;

namespace FluentCMS.Plugins;

// The body a route reads by itself, which the API explorer and so the OpenAPI document describe. It names no media type to
// routing: with one, as Accepts<T> gives, routing would refuse another with a bare 415 before the route answers its
// problem. MediaType is for the document alone, which core's operation transformer writes in place of application/json.
internal sealed record RequestBodyMetadata(Type RequestType, bool IsOptional, string? Description, string? MediaType) : IAcceptsMetadata
{
    public IReadOnlyList<string> ContentTypes => [];
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;

namespace FluentCMS.Core.Api.OpenApi;

// The JSON body a route reads by itself, which the API explorer and so the OpenAPI document describe. It names no media
// type: with one, as Accepts<T> gives, routing would refuse another with a bare 415 before the route answers its problem.
internal sealed record JsonRequestBody(Type RequestType, bool IsOptional, string? Description) : IAcceptsMetadata
{
    public IReadOnlyList<string> ContentTypes => [];
}

internal static class JsonRequestBodyExtensions
{
    public static RouteHandlerBuilder Takes<T>(this RouteHandlerBuilder builder, bool optional = false, string? description = null) =>
        builder.WithMetadata(new JsonRequestBody(typeof(T), optional, description));
}

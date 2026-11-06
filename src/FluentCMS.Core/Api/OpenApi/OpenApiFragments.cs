using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace FluentCMS.Core.Api.OpenApi;

// The document's additions are written as JSON, which is how a field type gives its fragment, and read into the
// document's object model here, so that a $ref becomes a reference of this document.
internal static class OpenApiFragments
{
    public static T Read<T>(JsonNode json, OpenApiDocument document, string what)
        where T : IOpenApiElement
    {
        var element = new OpenApiJsonReader().ReadFragment<T>(json, OpenApiSpecVersion.OpenApi3_1, document, out var diagnostic, new OpenApiReaderSettings());
        if (element is null || diagnostic.Errors.Count > 0)
        {
            var errors = string.Join("; ", diagnostic.Errors.Select(error => $"{error.Pointer}: {error.Message}"));
            throw new InvalidOperationException($"The OpenAPI document cannot hold {what}: {errors}");
        }

        return element;
    }
}

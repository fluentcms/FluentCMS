using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// A request body is a JSON object. It is read here rather than bound, so that a body the endpoint cannot read is a
// problem like any other. A key given twice is refused at any depth, since only one of the values could be kept.
internal static class RequestBody
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = JsonDepth.Max, AllowDuplicateProperties = false };

    public static async Task<JsonElement> ObjectAsync(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.HasJsonContentType())
        {
            throw new BadHttpRequestException("The body must be application/json.", StatusCodes.Status415UnsupportedMediaType);
        }

        JsonElement body;
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, Options, request.HttpContext.RequestAborted);
            body = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new BadHttpRequestException($"The body is not valid JSON: {exception.Message}", exception);
        }

        return body.ValueKind == JsonValueKind.Object ? body : throw new BadHttpRequestException("The body must be a JSON object.");
    }

    // The values stay JSON; the engine reads them against the fields of the type.
    public static ContentDocument Document(JsonElement json)
    {
        ContentDocument entry = [];
        foreach (var property in json.EnumerateObject())
        {
            entry[property.Name] = property.Value;
        }

        return entry;
    }
}

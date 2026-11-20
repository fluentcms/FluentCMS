using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace FluentCMS.Admin.Api;

internal sealed class ManagementApi(HttpClient http) : IManagementApi, IDisposable
{
    public async Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);
        using var response = await SendCoreAsync(request, cancellationToken);
        var value = await response.Content.ReadFromJsonAsync(answer, cancellationToken)
            ?? throw new JsonException($"{request.Method} {request.Path} answered {(int)response.StatusCode} with no body.");
        return new ApiResponse<T>(response.StatusCode, EntityTag(response), value) { Location = response.Headers.Location };
    }

    public async Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(request, cancellationToken);
        return new ApiResponse(response.StatusCode, EntityTag(response)) { Location = response.Headers.Location };
    }

    public void Dispose() => http.Dispose();

    private async Task<HttpResponseMessage> SendCoreAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.NoSite && request.Site is not null) || (request.NoLocale && request.Locale is not null))
        {
            throw new ArgumentException("A request that names its site or locale cannot also send none.", nameof(request));
        }

        if (request.Body is not null && request.Content is not null)
        {
            throw new ArgumentException("A request sends a JSON body or another content, not both.", nameof(request));
        }

        // The bearer token goes only to the Management API: a path that resolves anywhere else is refused.
        var baseAddress = http.BaseAddress!;
        if (!Uri.TryCreate(baseAddress, request.Path, out var target)
            || !baseAddress.IsBaseOf(target) || !string.Equals(target.GetLeftPart(UriPartial.Authority), baseAddress.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{request.Path}' leads out of the Management API.", nameof(request));
        }

        using var message = new HttpRequestMessage(request.Method, target);
        if (request.Body is { } body)
        {
            message.Content = JsonContent.Create<JsonNode>(body, ManagementJsonContext.Default.JsonNode);
        }
        else if (request.Content is { } content)
        {
            message.Content = new ReadOnlyMemoryContent(content.Bytes);
            message.Content.Headers.TryAddWithoutValidation("Content-Type", content.MediaType);
        }

        Add(message, ManagementHeaders.IfMatch, request.IfMatch is { } tag ? ManagementHeaders.StrongTag(tag) : null);
        Add(message, ManagementHeaders.IdempotencyKey, request.IdempotencyKey);
        Add(message, ManagementHeaders.Site, request.Site);
        Add(message, ManagementHeaders.Locale, request.Locale);

        // A request that names its site carries only the locale it names, since the server refuses a locale the site lacks.
        var namesItsSite = request.NoSite || request.Site is not null;
        message.Options.Set(ManagementHeaders.AddSessionSite, !namesItsSite);
        message.Options.Set(ManagementHeaders.AddSessionLocale, !namesItsSite && !request.NoLocale && request.Locale is null);

        var response = await http.SendAsync(message, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            throw new ApiProblemException(await ProblemAsync(response, cancellationToken));
        }
    }

    private static void Add(HttpRequestMessage message, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }
    }

    // The status of the answer is the problem's, whatever its body says. A member of the wrong type loses only itself where
    // it can: a current that is not an object is dropped, and a body that still does not read keeps its text members.
    private static async Task<ApiProblem> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await BodyProblemAsync(response, cancellationToken) with { RetryAfter = RetryAfter(response) };

    private static TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta < TimeSpan.Zero ? TimeSpan.Zero : delta,
        { Date: { } date } => date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero,
        _ => null,
    };

    private static async Task<ApiProblem> BodyProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) is JsonObject body)
                {
                    if (body["current"] is not (null or JsonObject))
                    {
                        body.Remove("current");
                    }

                    try
                    {
                        if (body.Deserialize(ManagementJsonContext.Default.ApiProblem) is { } problem)
                        {
                            return problem with { Status = status };
                        }
                    }
                    catch (JsonException)
                    {
                        return new ApiProblem
                        {
                            Status = status,
                            Code = Text(body, "code"),
                            Type = Text(body, "type"),
                            Title = Text(body, "title") ?? response.ReasonPhrase,
                            Detail = Text(body, "detail"),
                            TraceId = Text(body, "traceId"),
                        };
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return new ApiProblem { Status = status, Title = response.ReasonPhrase };
    }

    private static string? Text(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // Tag is the opaque value with its quotes, without the W/ of a weak tag.
    private static string? EntityTag(HttpResponseMessage response) => response.Headers.ETag?.Tag;
}

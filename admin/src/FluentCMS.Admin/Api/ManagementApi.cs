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
        return new ApiResponse<T>(response.StatusCode, EntityTag(response), value);
    }

    public async Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(request, cancellationToken);
        return new ApiResponse(response.StatusCode, EntityTag(response));
    }

    public void Dispose() => http.Dispose();

    private async Task<HttpResponseMessage> SendCoreAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(request.Method, new Uri(request.Path, UriKind.Relative));
        if (request.Body is { } body)
        {
            message.Content = JsonContent.Create<JsonNode>(body, ManagementJsonContext.Default.JsonNode);
        }

        Add(message, ManagementHeaders.IfMatch, request.IfMatch is { } tag ? ManagementHeaders.StrongTag(tag) : null);
        Add(message, ManagementHeaders.IdempotencyKey, request.IdempotencyKey);
        Add(message, ManagementHeaders.Site, request.Site);
        Add(message, ManagementHeaders.Locale, request.Locale);

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

    // The status of the answer is the problem's, whatever its body says; a body that is not a problem leaves only the status.
    private static async Task<ApiProblem> ProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                if (await response.Content.ReadFromJsonAsync(ManagementJsonContext.Default.ApiProblem, cancellationToken) is { } problem)
                {
                    return problem with { Status = status };
                }
            }
            catch (JsonException)
            {
            }
        }

        return new ApiProblem { Status = status, Title = response.ReasonPhrase };
    }

    // Tag is the opaque value with its quotes, without the W/ of a weak tag.
    private static string? EntityTag(HttpResponseMessage response) => response.Headers.ETag?.Tag;
}

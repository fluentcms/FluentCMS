using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.AiAssist.Admin.Api;

// What a streamed action ended with: the suggestion of its done event, or the problem of its error event.
internal sealed record AiOutcome(JsonNode? Suggestion, AiStreamProblem? Problem);

// The AI routes of the Management API (spec 5.5) over the admin's one client, and the media routes the alt text of an asset
// needs. A refusal before the provider is asked is the client's ApiProblemException, which is not sent again, a 429 included.
// A client that only reads, as the usage page, needs no event stream.
internal sealed class AiApi(IManagementApi api, IManagementEvents? events = null)
{
    // Each piece of the suggestion goes to onDelta as it arrives. The stream ends with its done or its error event; one that
    // ends with neither, as when the connection drops, is a failure of the answer.
    public async Task<AiOutcome> RunAsync(string action, AiActionBody body, Action<string> onDelta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(onDelta);
        var request = new ApiRequest(HttpMethod.Post, $"ai/actions/{Uri.EscapeDataString(action)}")
        {
            Body = JsonSerializer.SerializeToNode(body, AiJsonContext.Default.AiActionBody),
        };

        var stream = events ?? throw new InvalidOperationException("An action streams its suggestion, so its client needs IManagementEvents.");
        await foreach (var sent in stream.ReadAsync(request, cancellationToken))
        {
            switch (sent.Name)
            {
                case "delta" when JsonSerializer.Deserialize(sent.Data, AiJsonContext.Default.AiDelta) is { } delta:
                    onDelta(delta.Text);
                    break;
                case "done":
                    return new AiOutcome(JsonSerializer.Deserialize(sent.Data, AiJsonContext.Default.AiActionAnswer)?.Suggestion, null);
                case "error":
                    return new AiOutcome(null, JsonSerializer.Deserialize(sent.Data, AiJsonContext.Default.AiStreamProblem) ?? new AiStreamProblem());
            }
        }

        throw new HttpRequestException($"POST ai/actions/{action} ended before its last event.");
    }

    public async Task<AiUsageAnswer> UsageAsync(AiUsageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var answer = await api.SendAsync(new ApiRequest(HttpMethod.Get, "ai/usage" + query.ToQueryString()), AiJsonContext.Default.AiUsageAnswer, cancellationToken);
        return answer.Value;
    }

    // The asset with its alt text in the locale named.
    public async Task<AiMediaAsset> AssetAsync(Guid id, string? locale, CancellationToken cancellationToken)
    {
        var answer = await api.SendAsync(new ApiRequest(HttpMethod.Get, $"media/{id:D}") { Locale = locale }, AiJsonContext.Default.AiMediaAsset, cancellationToken);
        return answer.Value with { ETag = answer.EntityTag };
    }

    // In the locale the asset was read in, on its ETag: a change of the asset since is a 412, which is shown as any problem.
    public async Task SetAltAsync(AiMediaAsset asset, string alt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var request = new ApiRequest(HttpMethod.Patch, $"media/{asset.Id:D}") { Body = new JsonObject { ["alt"] = alt }, Locale = asset.Locale, IfMatch = asset.ETag };
        await api.SendAsync(request, cancellationToken);
    }
}

// The filters and the page of GET /ai/usage. From and To are days in UTC, as the daily budget counts them; To is the last day.
internal sealed record AiUsageQuery
{
    public const int PageSize = 50;

    public Guid? PrincipalId { get; init; }

    public string? Action { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public int Page { get; init; } = 1;

    public string ToQueryString()
    {
        List<string> parts = [];
        if (PrincipalId is { } principal)
        {
            parts.Add($"principalId={principal:D}");
        }

        if (Action is { Length: > 0 } action)
        {
            parts.Add($"action={Uri.EscapeDataString(action)}");
        }

        if (From is { } from)
        {
            parts.Add($"from={Instant(from)}");
        }

        if (To is { } to)
        {
            parts.Add($"to={Instant(to.AddDays(1))}");
        }

        parts.Add($"page={Page.ToString(CultureInfo.InvariantCulture)}");
        parts.Add($"pageSize={PageSize.ToString(CultureInfo.InvariantCulture)}");
        return "?" + string.Join('&', parts);
    }

    private static string Instant(DateOnly day) => Uri.EscapeDataString(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z");
}

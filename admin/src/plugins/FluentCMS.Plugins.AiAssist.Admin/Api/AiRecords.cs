using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FluentCMS.Plugins.AiAssist.Admin.Api;

// The records of the AI routes the half reads, its own rather than the server half's (spec 5.1), which the API tests hold to
// the reference host's OpenAPI document (spec 7). Each reads the members the half shows and skips the others.

// The body of POST /ai/actions/{action}: the field the suggestion is for and its text, or neither for the alt text of an
// asset alone.
internal sealed record AiActionBody
{
    public string? Type { get; init; }

    public string? Field { get; init; }

    public string? Locale { get; init; }

    public string? Text { get; init; }

    public AiActionOptions? Options { get; init; }
}

// The image of altText; the half sends no other option.
internal sealed record AiActionOptions
{
    public Guid? MediaId { get; init; }
}

// The done event of a stream, as the plain answer: the text, or for seo an object of a meta title and description.
internal sealed record AiActionAnswer
{
    public required JsonNode Suggestion { get; init; }
}

// A piece of the suggestion, as the delta event carries it.
internal sealed record AiDelta
{
    public required string Text { get; init; }
}

// The error event of a stream, the problem of a refusal after the provider was asked.
internal sealed record AiStreamProblem
{
    public string? Title { get; init; }

    public int Status { get; init; }

    public string? Detail { get; init; }

    public string? Code { get; init; }
}

internal sealed record AiUsageAnswer
{
    public required IReadOnlyList<AiUsageRow> Items { get; init; }

    public required long Total { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required AiUsageTotals Totals { get; init; }
}

// One call sent to a provider, with the name its principal had at the call.
internal sealed record AiUsageRow
{
    public required Guid Id { get; init; }

    public required string PrincipalKind { get; init; }

    public required Guid PrincipalId { get; init; }

    public string? PrincipalName { get; init; }

    public required string Action { get; init; }

    public string? Type { get; init; }

    public string? Field { get; init; }

    public string? Model { get; init; }

    public long PromptTokens { get; init; }

    public long CompletionTokens { get; init; }

    public long DurationMs { get; init; }

    public bool Succeeded { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

// The calls and tokens of every row the filter matches, in all and by principal and action.
internal sealed record AiUsageTotals
{
    public long Calls { get; init; }

    public long PromptTokens { get; init; }

    public long CompletionTokens { get; init; }

    public required IReadOnlyList<AiUsageGroup> Groups { get; init; }
}

internal sealed record AiUsageGroup
{
    public required string PrincipalKind { get; init; }

    public required Guid PrincipalId { get; init; }

    public required string Action { get; init; }

    public long Calls { get; init; }

    public long PromptTokens { get; init; }

    public long CompletionTokens { get; init; }
}

// An asset of the media library as far as its alt text needs it, in the locale it was read in.
internal sealed record AiMediaAsset
{
    public required Guid Id { get; init; }

    public required string FileName { get; init; }

    public required string MimeType { get; init; }

    public string? Title { get; init; }

    public string? Locale { get; init; }

    public string? Alt { get; init; }

    // The ETag of the read, which a save of the alt text sends back as If-Match.
    [JsonIgnore]
    public string? ETag { get; init; }

    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(Title) ? FileName : Title;

    [JsonIgnore]
    public bool IsImage => MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AiActionBody))]
[JsonSerializable(typeof(AiActionAnswer))]
[JsonSerializable(typeof(AiDelta))]
[JsonSerializable(typeof(AiStreamProblem))]
[JsonSerializable(typeof(AiUsageAnswer))]
[JsonSerializable(typeof(AiMediaAsset))]
[JsonSerializable(typeof(JsonObject))]
internal sealed partial class AiJsonContext : JsonSerializerContext;

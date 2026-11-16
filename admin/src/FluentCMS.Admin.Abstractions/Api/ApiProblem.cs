using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Api;

/// <summary>
/// A problem (RFC 9457) the Management API answered. Callers branch on <see cref="Status"/> and <see cref="Code"/> only;
/// <see cref="Title"/> and <see cref="Detail"/> are for people.
/// </summary>
public sealed record ApiProblem
{
    /// <summary>The HTTP status.</summary>
    public int Status { get; init; }

    /// <summary>The FCMS code, such as <c>FCMS3105</c>, or <see langword="null"/> for an answer that is not a FluentCMS problem.</summary>
    public string? Code { get; init; }

    /// <summary>The problem type, <c>urn:fluentcms:error:{code}</c>.</summary>
    public string? Type { get; init; }

    /// <summary>A short summary.</summary>
    public string? Title { get; init; }

    /// <summary>What happened, for people.</summary>
    public string? Detail { get; init; }

    /// <summary>The validation errors, each with the path of the value it is about.</summary>
    public IReadOnlyList<ApiProblemError> Errors { get; init; } = [];

    /// <summary>The entry as it is now, on a 412.</summary>
    public JsonObject? Current { get; init; }

    /// <summary>The trace id of the request, which a view offers to copy.</summary>
    public string? TraceId { get; init; }
}

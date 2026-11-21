using System.Net;

namespace FluentCMS.Admin.Api;

/// <summary>A successful answer of the Management API.</summary>
public class ApiResponse
{
    /// <summary>Creates an answer.</summary>
    /// <param name="status">The status.</param>
    /// <param name="entityTag">The entity tag, as <c>"7"</c>, or <see langword="null"/>.</param>
    public ApiResponse(HttpStatusCode status, string? entityTag)
    {
        Status = status;
        EntityTag = entityTag;
    }

    /// <summary>The status.</summary>
    public HttpStatusCode Status { get; }

    /// <summary>
    /// The <c>ETag</c> of the answer by its value, as <c>"7"</c>, weak or not; a save sends it back as
    /// <see cref="ApiRequest.IfMatch"/>. <see langword="null"/> when the answer has none.
    /// </summary>
    public string? EntityTag { get; }

    /// <summary>The <c>Location</c> of the answer, such as that of a 201 to a create, or <see langword="null"/> when it has none.</summary>
    public Uri? Location { get; init; }

    /// <summary>The <c>Date</c> of the answer, the server's clock, or <see langword="null"/> when the browser does not expose it.</summary>
    public DateTimeOffset? Date { get; init; }
}

/// <summary>A successful answer of the Management API with the record it holds.</summary>
/// <typeparam name="T">The record.</typeparam>
public sealed class ApiResponse<T> : ApiResponse
{
    /// <summary>Creates an answer.</summary>
    /// <param name="status">The status.</param>
    /// <param name="entityTag">The entity tag, as <c>"7"</c>, or <see langword="null"/>.</param>
    /// <param name="value">The record.</param>
    public ApiResponse(HttpStatusCode status, string? entityTag, T value)
        : base(status, entityTag)
    {
        Value = value;
    }

    /// <summary>The record.</summary>
    public T Value { get; }
}

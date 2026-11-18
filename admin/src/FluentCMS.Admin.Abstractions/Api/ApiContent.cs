namespace FluentCMS.Admin.Api;

/// <summary>
/// A body of an <see cref="ApiRequest"/> that is not JSON, such as the <c>multipart/form-data</c> of an upload, which the
/// caller writes. It is held in memory, so a request sent again after a 429 sends it again.
/// </summary>
public sealed class ApiContent
{
    /// <summary>Creates a body.</summary>
    /// <param name="mediaType">The <c>Content-Type</c>, with its parameters, such as <c>multipart/form-data; boundary=x</c>.</param>
    /// <param name="bytes">The bytes of the body.</param>
    public ApiContent(string mediaType, ReadOnlyMemory<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        MediaType = mediaType;
        Bytes = bytes;
    }

    /// <summary>The <c>Content-Type</c> of the body.</summary>
    public string MediaType { get; }

    /// <summary>The bytes of the body.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }
}

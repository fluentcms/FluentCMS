using FluentCMS.Content;

namespace FluentCMS.Agents;

/// <summary>
/// A resource as an MCP client sees it in the resource list, or a resource template when its URI has an expression such as
/// <c>{type}</c>.
/// </summary>
public sealed record McpResourceDescriptor
{
    /// <summary>Creates a descriptor.</summary>
    /// <param name="uri">The URI, such as <c>fluentcms://schema</c>, or a URI template (RFC 6570), such as <c>fluentcms://schema/{type}</c>.</param>
    /// <param name="name">A short name.</param>
    /// <exception cref="ArgumentException">The URI or the name is empty.</exception>
    public McpResourceDescriptor(string uri, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Uri = uri;
        Name = name;
    }

    /// <summary>The URI, or the URI template.</summary>
    public string Uri { get; }

    /// <summary>A short name.</summary>
    public string Name { get; }

    /// <summary>A name for people, or <see langword="null"/>.</summary>
    public string? Title { get; init; }

    /// <summary>What the resource holds, or <see langword="null"/>.</summary>
    public string? Description { get; init; }

    /// <summary>The media type of the contents, or <see langword="null"/>.</summary>
    public string? MimeType { get; init; }

    /// <summary>Whether <see cref="Uri"/> is a template, which is listed with the resource templates.</summary>
    public bool IsTemplate => Uri.Contains('{', StringComparison.Ordinal);
}

/// <summary>A read of a resource.</summary>
/// <param name="Operation">The operation of the read: its principal, site, locale and client.</param>
/// <param name="Uri">The URI the client asks for, which a provider of a template matches itself.</param>
public sealed record McpResourceRead(OperationContext Operation, string Uri);

/// <summary>The contents of a resource, text or bytes.</summary>
public sealed record McpResourceContent
{
    private McpResourceContent(string uri, string? mimeType, string? text, ReadOnlyMemory<byte> data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        Uri = uri;
        MimeType = mimeType;
        Text = text;
        Data = data;
    }

    /// <summary>The URI of the contents.</summary>
    public string Uri { get; }

    /// <summary>The media type, or <see langword="null"/>.</summary>
    public string? MimeType { get; }

    /// <summary>The text, or <see langword="null"/> for bytes.</summary>
    public string? Text { get; }

    /// <summary>The bytes; empty for text.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Contents of text.</summary>
    /// <param name="uri">The URI of the contents.</param>
    /// <param name="text">The text.</param>
    /// <param name="mimeType">The media type.</param>
    /// <returns>The contents.</returns>
    public static McpResourceContent FromText(string uri, string text, string mimeType = "text/plain")
    {
        ArgumentNullException.ThrowIfNull(text);

        return new McpResourceContent(uri, mimeType, text, default);
    }

    /// <summary>Contents of bytes.</summary>
    /// <param name="uri">The URI of the contents.</param>
    /// <param name="data">The bytes.</param>
    /// <param name="mimeType">The media type.</param>
    /// <returns>The contents.</returns>
    public static McpResourceContent FromBytes(string uri, ReadOnlyMemory<byte> data, string mimeType = "application/octet-stream") =>
        new(uri, mimeType, null, data);
}

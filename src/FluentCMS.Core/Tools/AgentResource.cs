using System.Text.RegularExpressions;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Tools;

// One resource, at a fixed URI or at a URI template whose {name} parts each match one path segment (RFC 6570, level 1).
// Its description is written for an LLM reader, as a tool's is (MCP-02, MCP-03).
public sealed partial class AgentResource
{
    private readonly Func<ResourceRead, CancellationToken, Task<Result<string>>> _read;
    private readonly Regex _pattern;
    private readonly IReadOnlyList<string> _parameters;

    public AgentResource(
        string uriTemplate,
        string name,
        string description,
        string mimeType,
        Func<ResourceRead, CancellationToken, Task<Result<string>>> read)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uriTemplate);
        if (!ToolNames.IsName(name))
        {
            throw new ArgumentException($"'{name}' is not a resource name; use lowercase words joined by underscores, such as 'schema_document'.", nameof(name));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(read);

        UriTemplate = uriTemplate;
        Name = name;
        Description = description;
        MimeType = mimeType;
        _read = read;
        _parameters = [.. Parameter().Matches(uriTemplate).Select(match => match.Groups[1].Value)];
        _pattern = new Regex(
            "^" + string.Concat(Parameter().Split(uriTemplate).Select((part, index) => index % 2 == 0 ? Regex.Escape(part) : $"(?<{part}>[^/?#]+)")) + "$",
            RegexOptions.CultureInvariant);
    }

    public string UriTemplate { get; }

    public string Name { get; }

    public string Description { get; }

    public string MimeType { get; }

    public bool IsTemplated => _parameters.Count > 0;

    // The template's parameters for a URI it matches, unescaped; null for any other URI.
    public IReadOnlyDictionary<string, string>? Match(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var match = _pattern.Match(uri);
        return match.Success
            ? _parameters.ToDictionary(name => name, name => Uri.UnescapeDataString(match.Groups[name].Value), StringComparer.Ordinal)
            : null;
    }

    public Task<Result<string>> ReadAsync(ResourceRead read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        return _read(read, cancellationToken);
    }

    [GeneratedRegex("\\{([a-z][a-zA-Z0-9]*)\\}")]
    private static partial Regex Parameter();
}

public sealed record ResourceRead(string Uri, IReadOnlyDictionary<string, string> Parameters, IServiceProvider Services);

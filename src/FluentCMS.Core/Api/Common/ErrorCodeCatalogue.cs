using System.Collections.Frozen;

namespace FluentCMS.Core.Api.Common;

// The codes of one host: the engine's, then the ones its plugins added with their status and title.
internal sealed class ErrorCodeCatalogue(IReadOnlyDictionary<string, ErrorCodeInfo> plugins)
{
    private readonly FrozenDictionary<string, ErrorCodeInfo> _plugins = plugins.ToFrozenDictionary(StringComparer.Ordinal);

    public static ErrorCodeCatalogue Core { get; } = new(new Dictionary<string, ErrorCodeInfo>());

    public ErrorCodeInfo? Find(string? code) =>
        ErrorCodeRegistry.Find(code) ?? (code is not null ? _plugins.GetValueOrDefault(code) : null);
}

using Microsoft.Extensions.Configuration;

namespace FluentCMS.Plugins.AiAssist;

// Bound from FluentCms:Ai. The key of a provider that a site's settings do not hold is FluentCms:Ai:{Provider}:ApiKey,
// such as FluentCms:Ai:OpenAi:ApiKey, and it is sent only to FluentCms:Ai:{Provider}:Endpoint, OpenAI's own for OpenAi
// when unset and needed for the other providers.
public sealed class AiOptions
{
    public const string Section = "FluentCms:Ai";

    // The endpoints a site may name in its settings. Empty, the default, leaves a site OpenAI at its own endpoint only, so
    // that a site administrator cannot point the server at an address inside the network.
    public IList<string> AllowedEndpoints { get; } = [];

    // The most characters of text an action takes.
    public int MaxInputLength { get; set; } = 20_000;

    // How long the usage log keeps a row.
    public TimeSpan UsageRetention { get; set; } = TimeSpan.FromDays(90);

    public bool IsValid() =>
        MaxInputLength is >= 1 and <= 1_000_000
        && UsageRetention > TimeSpan.Zero
        && AllowedEndpoints.All(endpoint => AiProviderEndpoints.Parse(endpoint) is not null);
}

// An endpoint is an absolute http or https URL with no user, query or fragment. Two are the same when their scheme, host,
// port and path are, whatever the case of the host and with or without a slash at the end.
internal static class AiProviderEndpoints
{
    public const string Shape = "An endpoint is an absolute http or https URL with no user name, password, query or fragment.";

    public static Uri? Parse(string? endpoint) =>
        Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.UserInfo.Length == 0
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
            ? uri
            : null;

    public static string Canonical(Uri endpoint) => $"{endpoint.Scheme}://{endpoint.IdnHost}:{endpoint.Port}{endpoint.AbsolutePath.TrimEnd('/')}";

    // The endpoint the configuration's key of a provider is for. Null for a provider with neither.
    public static Uri? Configured(IConfiguration configuration, AiProvider provider)
    {
        var named = configuration[$"{AiOptions.Section}:{provider}:Endpoint"];
        return string.IsNullOrWhiteSpace(named)
            ? provider == AiProvider.OpenAi ? AiClientFactory.OpenAiEndpoint : null
            : Parse(named);
    }

    // A key in the configuration names the endpoint it is for, OpenAI's excepted, and an endpoint there has the shape of one.
    public static bool AreConfigured(IConfiguration configuration) =>
        Enum.GetValues<AiProvider>().Where(provider => provider != AiProvider.None).All(provider =>
        {
            var endpoint = configuration[$"{AiOptions.Section}:{provider}:Endpoint"];
            var key = configuration[$"{AiOptions.Section}:{provider}:ApiKey"];
            return string.IsNullOrWhiteSpace(endpoint)
                ? provider == AiProvider.OpenAi || string.IsNullOrWhiteSpace(key)
                : Parse(endpoint) is not null;
        });

    public static bool IsAllowed(Uri endpoint, IEnumerable<string> allowed)
    {
        var canonical = Canonical(endpoint);
        return allowed.Select(Parse).Any(other => other is not null && string.Equals(Canonical(other), canonical, StringComparison.Ordinal));
    }
}

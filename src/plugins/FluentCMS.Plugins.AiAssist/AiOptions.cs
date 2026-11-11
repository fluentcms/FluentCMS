namespace FluentCMS.Plugins.AiAssist;

// Bound from FluentCms:Ai. The key of a provider that a site's settings do not hold is FluentCms:Ai:{Provider}:ApiKey,
// such as FluentCms:Ai:OpenAi:ApiKey.
public sealed class AiOptions
{
    public const string Section = "FluentCms:Ai";

    // The endpoints a site may name in its settings. Empty, the default, leaves a site OpenAI and Anthropic at their own
    // endpoints only, so that a site administrator cannot point the server at an address inside the network.
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

    public static bool IsAllowed(Uri endpoint, IEnumerable<string> allowed)
    {
        var canonical = Canonical(endpoint);
        return allowed.Select(Parse).Any(other => other is not null && string.Equals(Canonical(other), canonical, StringComparison.Ordinal));
    }
}

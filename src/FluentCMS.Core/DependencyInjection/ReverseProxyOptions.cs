using System.Net;

namespace FluentCMS.Core.DependencyInjection;

// FluentCms:ReverseProxy. What a host needs when a proxy, a CDN or a sub-path sits in front of it.
public sealed class ReverseProxyOptions
{
    public const string Section = "ReverseProxy";

    // The path the host is served under, such as /cms. Empty serves it from the root. A FluentCMS route is then found
    // only under it, the way a proxy that keeps the prefix sends it.
    public string? PathBase { get; set; }

    public ProxyForwardedHeadersOptions ForwardedHeaders { get; } = new();

    public ProxyHttpsRedirectionOptions HttpsRedirection { get; } = new();

    public ProxyHstsOptions Hsts { get; } = new();

    internal void Validate()
    {
        if (!string.IsNullOrEmpty(PathBase) && !IsPathBase(PathBase))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{nameof(PathBase)} is '{PathBase}'; it must start with a slash and must not end with one, such as /cms, and hold no query, fragment or space.");
        }

        ForwardedHeaders.Validate(nameof(ForwardedHeaders));
        HttpsRedirection.Validate(nameof(HttpsRedirection));
        Hsts.Validate(nameof(Hsts));
    }

    internal static bool IsPathBase(string value) =>
        value.Length > 1
        && value[0] == '/'
        && value[^1] != '/'
        && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '?' or '#' or '\\')
        && !value.Contains("//", StringComparison.Ordinal);
}

// The X-Forwarded-* headers of a proxy. Off by default: a header from a caller that is not a trusted proxy would let it
// choose the scheme and the client address the host sees.
public sealed class ProxyForwardedHeadersOptions
{
    public bool Enabled { get; set; }

    public bool ForwardFor { get; set; } = true;

    public bool ForwardProto { get; set; } = true;

    public bool ForwardHost { get; set; }

    // Addresses of the proxies to trust. With no proxy and no network listed, only the loopback addresses are trusted.
    public IList<string> KnownProxies { get; } = [];

    // Networks in CIDR notation, such as 10.0.0.0/8.
    public IList<string> KnownNetworks { get; } = [];

    // How many proxies in a row may add to X-Forwarded-For: 1 for one proxy, 2 for a CDN in front of one.
    public int ForwardLimit { get; set; } = 1;

    internal void Validate(string key)
    {
        if (Enabled && !ForwardFor && !ForwardProto && !ForwardHost)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{key}:{nameof(Enabled)} is true, but {nameof(ForwardFor)}, {nameof(ForwardProto)} and {nameof(ForwardHost)} are all false; turn on the headers the proxy sends.");
        }

        if (ForwardLimit < 1)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{key}:{nameof(ForwardLimit)} is {ForwardLimit}; it must be at least 1.");
        }

        for (var index = 0; index < KnownProxies.Count; index++)
        {
            if (!ProxyAddress.TryParse(KnownProxies[index], out _))
            {
                throw new FluentCmsConfigurationException(
                    HostErrorCodes.InvalidOption,
                    $"{key}:{nameof(KnownProxies)}:{index} is '{KnownProxies[index]}'; it must be an IP address.");
            }
        }

        for (var index = 0; index < KnownNetworks.Count; index++)
        {
            if (!ProxyAddress.TryParseNetwork(KnownNetworks[index], out _))
            {
                throw new FluentCmsConfigurationException(
                    HostErrorCodes.InvalidOption,
                    $"{key}:{nameof(KnownNetworks)}:{index} is '{KnownNetworks[index]}'; it must be a network in CIDR notation such as 10.0.0.0/8.");
            }
        }
    }

    internal bool TrustsEveryAddress =>
        KnownNetworks.Any(network => ProxyAddress.TryParseNetwork(network, out var parsed) && parsed.PrefixLength == 0);
}

// Redirects an http request to https. Off by default, because behind a proxy that ends TLS every request reaches the host
// as http, and redirecting it would loop unless the proxy forwards the scheme.
public sealed class ProxyHttpsRedirectionOptions
{
    public bool Enabled { get; set; }

    // The port of the https address the redirect names, such as 443. Empty leaves it to the framework, which reads HTTPS_PORT or
    // ASPNETCORE_HTTPS_PORT and then the https address of the server, and redirects nowhere when it finds none, as behind a proxy.
    public int? Port { get; set; }

    internal void Validate(string key)
    {
        if (Port is < 1 or > 65535)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{key}:{nameof(Port)} is {Port}; it must be between 1 and 65535.");
        }
    }
}

// Strict-Transport-Security on https responses. Empty Enabled is on outside Development, as in an ASP.NET Core template.
public sealed class ProxyHstsOptions
{
    public bool? Enabled { get; set; }

    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(30);

    public bool IncludeSubDomains { get; set; }

    public bool Preload { get; set; }

    internal void Validate(string key)
    {
        if (MaxAge <= TimeSpan.Zero)
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{key}:{nameof(MaxAge)} is {MaxAge}; it must be longer than zero.");
        }
    }
}

// The addresses of proxies are written in full. IPAddress.TryParse also takes legacy forms, where "1" is 0.0.0.1, which in a
// list of trusted proxies would trust an address nobody meant.
internal static class ProxyAddress
{
    public static bool TryParse(string? text, out IPAddress address)
    {
        address = IPAddress.None;
        if (string.IsNullOrEmpty(text) || !IPAddress.TryParse(text, out var parsed))
        {
            return false;
        }

        var valid = parsed.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => parsed.ToString() == text,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => text.Contains(':', StringComparison.Ordinal) && !text.Contains('%', StringComparison.Ordinal),
            _ => false,
        };

        address = parsed;
        return valid;
    }

    public static bool TryParseNetwork(string? text, out IPNetwork network)
    {
        network = default;
        var slash = text?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        if (text is null || slash < 1)
        {
            return false;
        }

        var prefix = text[(slash + 1)..];
        return TryParse(text[..slash], out _)
            && prefix.Length is >= 1 and <= 3
            && prefix.All(char.IsAsciiDigit)
            && IPNetwork.TryParse(text, out network);
    }
}

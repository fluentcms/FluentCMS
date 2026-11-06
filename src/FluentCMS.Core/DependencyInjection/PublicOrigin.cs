using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Core.DependencyInjection;

// FluentCms:PublicOrigin, the address users reach the instance at, such as https://cms.example.com. Links in emails and the
// issuer of the authorization server are built from it, so a forged Host header cannot change them. Without it they follow
// the request's scheme, host and path base.
internal sealed class PublicOrigin
{
    public const string Key = "PublicOrigin";

    // The scheme, host, port and FluentCms:ReverseProxy:PathBase, with no trailing slash. Null when it is not set.
    public string? Base { get; private set; }

    public Uri? Issuer => Base is null ? null : new Uri(Base, UriKind.Absolute);

    public Uri Link(HttpRequest request, string path) =>
        new((Base ?? $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}") + path, UriKind.Absolute);

    // The origin may name the path base or leave it out; any other path would send links where the host does not serve.
    internal void Resolve(string? configured, ReverseProxyOptions proxy, IHostEnvironment? environment)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            Base = null;
            return;
        }

        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw Invalid(configured, "it must be an absolute https URL such as https://cms.example.com, with no user, query or fragment.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && environment?.IsDevelopment() != true)
        {
            throw Invalid(configured, "it must be an https URL outside Development.");
        }

        var pathBase = proxy.PathBase ?? "";
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length > 0 && !string.Equals(path, pathBase, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(configured, pathBase.Length == 0
                ? $"its path must be empty, because {FluentCmsOptions.Section}:{ReverseProxyOptions.Section}:{nameof(ReverseProxyOptions.PathBase)} is not set. Set the path base and keep the prefix at the proxy."
                : $"its path must be empty or {FluentCmsOptions.Section}:{ReverseProxyOptions.Section}:{nameof(ReverseProxyOptions.PathBase)} ('{pathBase}'), which is added when it is left out.");
        }

        Base = uri.GetLeftPart(UriPartial.Authority) + pathBase;
    }

    private static FluentCmsConfigurationException Invalid(string configured, string reason) =>
        new(HostErrorCodes.InvalidOption, $"{FluentCmsOptions.Section}:{Key} is '{configured}'; {reason}");
}

using System.Text.RegularExpressions;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Admin.Hosting;

// Registers the admin as a public client of the admin pool, which signs in with the code flow and PKCE (admin spec 2.4, 6.1),
// after FluentCms:Clients is bound, so that a client the configuration or the host lists under cms.Clients.AdminClientId wins
// whole. Its addresses are FluentCms:PublicOrigin's. Development without one has no request to read at start, so it registers
// the addresses the host listens on, from urls or http_ports; elsewhere the request's host is never used, and the admin
// needs the public origin.
internal sealed partial class AdminClientRegistration(
    AdminOptions admin,
    PublicOrigin origin,
    ReverseProxyOptions proxy,
    IHostEnvironment environment,
    IConfiguration configuration) : IPostConfigureOptions<ClientOptions>
{
    // Kestrel's address when nothing names one.
    private const string DefaultAddress = "http://localhost:5000";

    public void PostConfigure(string? name, ClientOptions options)
    {
        if (name != Options.DefaultName || !admin.Enabled)
        {
            return;
        }

        var origins = Origins();
        if (!AdminOptions.ClientIdPattern().IsMatch(options.AdminClientId))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{FluentCmsOptions.Section}:{ClientOptions.Section}:{nameof(ClientOptions.AdminClientId)} is '{options.AdminClientId}'; the admin's config.json takes a client id of visible ASCII characters without spaces.");
        }

        if (options.Applications.ContainsKey(options.AdminClientId))
        {
            return;
        }

        var client = new ClientRegistration { DisplayName = "FluentCMS admin", Pool = ClientPool.Admins };
        foreach (var hostBase in origins)
        {
            client.RedirectUris.Add(hostBase + AdminSite.Path + AdminSite.CallbackPath);
            client.PostLogoutRedirectUris.Add(hostBase + AdminSite.Path + "/");
        }

        foreach (var scope in AdminSite.Scopes)
        {
            client.Scopes.Add(scope);
        }

        options.Applications[options.AdminClientId] = client;
    }

    // The scheme, host, port and path base of each address the admin is reached at, without a trailing slash.
    private List<string> Origins()
    {
        // The admin reads its addresses in config.json with the rules of admin spec 2.5, which refuse what the path base and
        // the public origin may hold otherwise: characters such as parentheses, and a host that is not ASCII.
        var pathBase = proxy.PathBase ?? "";
        if (pathBase.Length > 0 && !PathBasePattern().IsMatch(pathBase))
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{FluentCmsOptions.Section}:{ReverseProxyOptions.Section}:{nameof(ReverseProxyOptions.PathBase)} is '{pathBase}', which the admin cannot take in its addresses; use letters, digits and - . _ ~ / only, or turn the admin off.");
        }

        if (origin.Base is { } publicBase)
        {
            if (AdminOptions.HttpUrl(publicBase) is null)
            {
                throw new FluentCmsConfigurationException(
                    HostErrorCodes.InvalidOption,
                    $"{FluentCmsOptions.Section}:{PublicOrigin.Key} is '{publicBase}', which the admin cannot take in its addresses; write an internationalized host name in its xn-- form, such as https://xn--mnchen-3ya.example, or turn the admin off.");
            }

            return [publicBase];
        }

        if (!environment.IsDevelopment())
        {
            throw new FluentCmsConfigurationException(
                HostErrorCodes.InvalidOption,
                $"{FluentCmsOptions.Section}:{AdminOptions.Section}:{nameof(AdminOptions.Enabled)} is true and {FluentCmsOptions.Section}:{PublicOrigin.Key} is not set. Outside Development the admin's sign-in addresses and its config.json are built from the public origin, never from a request's host; set it, or turn the admin off.");
        }

        return [.. ListeningAddresses().Select(address => address + pathBase).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private IEnumerable<string> ListeningAddresses()
    {
        var urls = configuration["urls"];
        IEnumerable<string> addresses = !string.IsNullOrWhiteSpace(urls)
            ? urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Ports("http_ports", "http").Concat(Ports("https_ports", "https"));

        var found = false;
        foreach (var address in addresses)
        {
            // A wildcard host, as in http://*:8080, is reached on this machine as localhost.
            if (Uri.TryCreate(AnyHost().Replace(address, "${scheme}://localhost"), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                found = true;
                yield return uri.GetLeftPart(UriPartial.Authority);
            }
        }

        if (!found)
        {
            yield return DefaultAddress;
        }
    }

    private IEnumerable<string> Ports(string key, string scheme) =>
        (configuration[key] ?? "").Split(';', ',')
            .Select(port => port.Trim())
            .Where(port => int.TryParse(port, out var number) && number is > 0 and <= 65535)
            .Select(port => $"{scheme}://localhost:{port}");

    // Unreserved characters only, which every address of the admin writes alike, escaped or not.
    [GeneratedRegex(@"^(/[A-Za-z0-9._~-]+)+\z")]
    private static partial Regex PathBasePattern();

    [GeneratedRegex(@"^(?<scheme>https?)://(?:\*|\+|0\.0\.0\.0|\[::\])(?=:|/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AnyHost();
}

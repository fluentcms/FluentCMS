using System.Globalization;
using FluentCMS.Core.Models;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Models;

// An entry is a host, which also allows its subdomains, or a host and a port, such as localhost:11434, which allows that port only.
// Hosts are compared in punycode and lowercase, as a request's host is.
internal sealed class OutboundHosts(IOptions<OutboundOptions> options) : IOutboundHosts
{
    private readonly (string Host, int? Port)[] _allowed = options.Value.AllowedHosts.Select(Parse).OfType<(string, int?)>().Distinct().ToArray();

    public bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return uri.IsAbsoluteUri
            && Normalize(uri.IdnHost) is { } host
            && _allowed.Any(entry => (host == entry.Host || host.EndsWith("." + entry.Host, StringComparison.Ordinal))
                && (entry.Port is null || entry.Port == uri.Port));
    }

    private static (string Host, int? Port)? Parse(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry) || !Uri.TryCreate("scheme://" + entry.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return Normalize(uri.IdnHost) is { } host ? (host, uri.IsDefaultPort ? null : uri.Port) : null;
    }

    private static string? Normalize(string host)
    {
        try
        {
            return new IdnMapping().GetAscii(host.Trim('[', ']')).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

// Refuses a request to a host that is not allowed before it leaves the process, whatever sent it (SEC-05). The primary handler does
// not follow redirects, so a redirect to another host comes back through here instead of being followed out of sight.
internal sealed class OutboundAllowListHandler(IOutboundHosts outbound) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } uri || !outbound.IsAllowed(uri))
        {
            throw new HttpRequestException(
                $"Requests to {request.RequestUri?.GetLeftPart(UriPartial.Authority) ?? "a URL without a host"} are not allowed; "
                + "an administrator adds the host to Outbound__AllowedHosts.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

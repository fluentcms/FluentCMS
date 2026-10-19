using System.Globalization;
using FluentCMS.Core.Models;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Models;

// Hosts are compared in punycode and lowercase, as a request's host is; an allowed host allows its subdomains.
internal sealed class OutboundHosts(IOptions<OutboundOptions> options) : IOutboundHosts
{
    private readonly string[] _allowed = options.Value.AllowedHosts.Select(Normalize).OfType<string>().Distinct().ToArray();

    public bool IsAllowed(string host)
    {
        return Normalize(host) is { } normalized
            && _allowed.Any(allowed => normalized == allowed || normalized.EndsWith("." + allowed, StringComparison.Ordinal));
    }

    private static string? Normalize(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        try
        {
            return new IdnMapping().GetAscii(host.Trim().Trim('[', ']')).ToLowerInvariant();
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
        var host = request.RequestUri?.IdnHost;
        if (host is null || !outbound.IsAllowed(host))
        {
            throw new HttpRequestException($"Requests to {host ?? "a URL without a host"} are not allowed; an administrator adds the host to Outbound__AllowedHosts.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

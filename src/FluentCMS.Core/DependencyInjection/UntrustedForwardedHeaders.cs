using System.Net;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.DependencyInjection;

// A request that carries X-Forwarded-For or -Proto from an address the host does not trust is one the framework ignores
// with a Debug line, so behind a proxy that is not listed every caller shares the proxy's address and its rate limits.
// A direct caller can send the headers too, and the host cannot tell it from a proxy, so the first few distinct addresses
// are reported, each once per process, rather than the first alone, which a forged header could take.
internal sealed partial class UntrustedForwardedHeaders(StartupReportHolder report, ILogger<UntrustedForwardedHeaders> logger)
{
    public const string Id = "forwarded-headers-untrusted";
    public const int MaxAddresses = 4;

    private readonly Lock _gate = new();
    private readonly List<string> _addresses = [];
    private volatile bool _full;

    // Null forwarding means forwarded headers are off, so any of them is ignored.
    public void Inspect(HttpContext context, ForwardedHeadersOptions? forwarding)
    {
        if (_full)
        {
            return;
        }

        var headers = context.Request.Headers;
        var forwardFor = forwarding is null || forwarding.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor);
        var forwardProto = forwarding is null || forwarding.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto);
        if (!(forwardFor && headers.ContainsKey(forwarding?.ForwardedForHeaderName ?? ForwardedHeadersDefaults.XForwardedForHeaderName))
            && !(forwardProto && headers.ContainsKey(forwarding?.ForwardedProtoHeaderName ?? ForwardedHeadersDefaults.XForwardedProtoHeaderName)))
        {
            return;
        }

        // The framework applies the headers when it cannot tell the address, as on an in-process test server.
        var address = context.Connection.RemoteIpAddress;
        if (address is null || (forwarding is not null && Trusted(forwarding, address)))
        {
            return;
        }

        // In full, as KnownProxies takes it.
        var text = (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
        string[] seen;
        lock (_gate)
        {
            if (_full || _addresses.Contains(text))
            {
                return;
            }

            _addresses.Add(text);
            _full = _addresses.Count == MaxAddresses;
            seen = [.. _addresses];
        }

        Untrusted(logger, text, Message([text], forwarding is null, full: false));
        report.Add(new InsecureDefault { Id = Id, Message = Message(seen, forwarding is null, seen.Length == MaxAddresses) });
    }

    // Trusting a forged address would let any caller choose its address and scheme, so the advice to list one comes
    // after the check that it is the instance's proxy.
    private static string Message(string[] addresses, bool off, bool full)
    {
        var from = addresses.Length == 1 ? $"A request from {addresses[0]}" : $"Requests from {string.Join(", ", addresses)}";
        var more = full ? $" Only the first {MaxAddresses} addresses are named." : "";
        var ignored = off
            ? "carried X-Forwarded-For or X-Forwarded-Proto while forwarded headers are off, so the host ignored them."
            : $"carried X-Forwarded-For or X-Forwarded-Proto from outside {FluentCmsOptions.Section}:{ReverseProxyOptions.Section}:ForwardedHeaders:KnownProxies and KnownNetworks (loopback only when both are empty), so the host ignored them.";
        var fix = off
            ? $"turn on {FluentCmsOptions.Section}:{ReverseProxyOptions.Section}:ForwardedHeaders:Enabled and list it in KnownProxies or KnownNetworks"
            : "list it in KnownProxies or KnownNetworks";
        return $"{from} {ignored} Behind a proxy the host does not trust, every caller has the proxy's address and shares its rate limits. Only once you have confirmed that an address is the instance's own proxy, {fix}. Any other address reached the host directly, around the proxy, with headers it chose: do not trust it, and close that route, for example with a firewall rule or a network policy.{more}";
    }

    // As ForwardedHeadersMiddleware decides it, an IPv4 address mapped to IPv6 matching as either.
    private static bool Trusted(ForwardedHeadersOptions forwarding, IPAddress address) =>
        (address.IsIPv4MappedToIPv6 && Trusted(forwarding, address.MapToIPv4()))
        || forwarding.KnownProxies.Contains(address)
        || forwarding.KnownIPNetworks.Any(network => network.Contains(address));

    [LoggerMessage(
        EventId = StartupReportEvents.UntrustedForwardedHeaders,
        EventName = "UntrustedForwardedHeaders",
        Level = LogLevel.Warning,
        Message = "Insecure default forwarded-headers-untrusted from {Address}: {Message}")]
    private static partial void Untrusted(ILogger logger, string address, string message);
}

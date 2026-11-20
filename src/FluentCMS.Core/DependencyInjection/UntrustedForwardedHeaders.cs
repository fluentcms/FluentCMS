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
// The first such request is reported, once per process, in the log and the startup report.
internal sealed partial class UntrustedForwardedHeaders(StartupReportHolder report, ILogger<UntrustedForwardedHeaders> logger)
{
    public const string Id = "forwarded-headers-untrusted";

    private int _seen;
    private volatile InsecureDefault? _item;

    public bool Seen => Volatile.Read(ref _seen) == 1;

    // For a report made after the first such request.
    public InsecureDefault? Item => _item;

    // Null forwarding means forwarded headers are off, so any of them is ignored.
    public void Inspect(HttpContext context, ForwardedHeadersOptions? forwarding)
    {
        if (Seen)
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
        if (address is null || (forwarding is not null && Trusted(forwarding, address)) || Interlocked.Exchange(ref _seen, 1) == 1)
        {
            return;
        }

        // In full, as KnownProxies takes it.
        var partition = (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
        var message = forwarding is null
            ? $"A request from {partition} carried X-Forwarded-For or X-Forwarded-Proto, and forwarded headers are off, so the host ignores them. If that address is a proxy of the instance, every caller has its address and shares its rate limits: turn on {FluentCmsOptions.Section}:{ReverseProxyOptions.Section}:ForwardedHeaders:Enabled and list the proxy in KnownProxies or KnownNetworks."
            : $"A request from {partition}, which is not in {FluentCmsOptions.Section}:{ReverseProxyOptions.Section}:ForwardedHeaders:KnownProxies or KnownNetworks (loopback only when both are empty), carried X-Forwarded-For or X-Forwarded-Proto, so the host ignores them. If that address is a proxy of the instance, every caller has its address and shares its rate limits: list the proxy or its network.";
        Untrusted(logger, partition, message);

        var item = new InsecureDefault { Id = Id, Message = message };
        _item = item;
        if (report.Current is { } current && current.InsecureDefaults.All(existing => existing.Id != Id))
        {
            report.Set(current with { InsecureDefaults = [.. current.InsecureDefaults, item] });
        }
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

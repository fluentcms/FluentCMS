using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace FluentCMS.Plugins.Webhooks;

// The target of a webhook: an absolute https URL (http in Development) whose host is a public address, checked when the
// webhook is saved and again on the resolved address of every connection, so that DNS cannot point a saved webhook at the
// host's own network. Redirects are never followed.
internal static class WebhookAddresses
{
    public static string? UrlRefusal(string url, bool development)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown)
        {
            return "The URL must be absolute, such as https://example.com/hooks.";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(development && uri.Scheme == Uri.UriSchemeHttp))
        {
            return development ? "The URL must use https or http." : "The URL must use https.";
        }

        return uri.UserInfo.Length > 0 ? "The URL must not carry a user name or password." : null;
    }

    // Null when the host resolves to public addresses only, or does not resolve now: delivery checks it again.
    public static async Task<string?> AddressRefusalAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.IdnHost, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(uri.IdnHost, cancellationToken);
        }
        catch (SocketException)
        {
            return null;
        }

        return addresses.All(IsPublic) ? null : NotPublic;
    }

    public const string NotPublic = $"The host of the URL is not a public address; {WebhooksOptions.Section}:{nameof(WebhooksOptions.AllowPrivateNetworks)} allows it.";

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var length))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork && length == 4)
        {
            return !(bytes[0] is 0 or 10 or 127 or >= 224
                || (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && (bytes[1] & 0xF0) == 16)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                || (bytes[0] == 198 && (bytes[1] & 0xFE) == 18));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 || length != 16)
        {
            return false;
        }

        // The unspecified address, loopback and the IPv4-compatible forms, then NAT64 and 6to4, which carry an IPv4 address,
        // and Teredo, which tunnels to one.
        if (bytes[..12].IndexOfAnyExcept((byte)0) < 0)
        {
            return false;
        }

        if (bytes[..12].SequenceEqual(Nat64Prefix))
        {
            return IsPublic(new IPAddress(bytes[12..16]));
        }

        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return IsPublic(new IPAddress(bytes[2..6]));
        }

        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0)
        {
            return false;
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast);
    }

    public static SocketsHttpHandler CreateHandler(bool allowPrivateNetworks) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, allowPrivateNetworks, cancellationToken),
    };

    private static readonly byte[] Nat64Prefix = [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];

    private static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, bool allowPrivateNetworks, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        if (!allowPrivateNetworks && !addresses.All(IsPublic))
        {
            throw new WebhookAddressException(NotPublic);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

// A connection refused before it was made, because its address is not public.
internal sealed class WebhookAddressException(string message) : IOException(message);

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

    public static bool IsPublic(IPAddress address) => PublicAddresses.IsPublic(address);

    public static SocketsHttpHandler CreateHandler(bool allowPrivateNetworks) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, allowPrivateNetworks, cancellationToken),
    };

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

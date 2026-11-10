using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Mcp;

// A fetch that was refused, or that failed, with the code the tool answers.
internal sealed class McpDownloadException(string code, string message) : IOException(message)
{
    public string Code { get; } = code;
}

// What sends the requests of McpDownloads. A host has none registered and gets the guarded handler; a test registers one.
internal sealed class McpDownloadTransport(HttpMessageHandler handler)
{
    public HttpMessageHandler Handler { get; } = handler;
}

// Fetches the file a media_upload names by URL, without letting the URL reach the host's own network: https only, no
// user name or password, and every address the host name resolves to must be public, checked on the connection itself, so
// that DNS cannot answer one address to a check and another to the connection. A redirect is followed up to
// MaxRedirects times and held to the same rules, so none leads to a private address. No proxy, no cookies and no
// credentials are used. The body is read by whoever reads the stream, which is where the upload limits apply.
internal sealed class McpDownloads : IDisposable
{
    public const int MaxRedirects = 3;

    private const string NotPublic = "The host of the URL is not a public address.";

    private readonly HttpMessageInvoker _client;
    private readonly TimeSpan _timeout;

    public McpDownloads(IOptions<McpOptions> options, IEnumerable<McpDownloadTransport> transports)
    {
        _client = new HttpMessageInvoker(transports.LastOrDefault()?.Handler ?? CreateHandler(), disposeHandler: true);
        _timeout = options.Value.UploadTimeout;
    }

    // The URL as one that may be fetched, before anything is sent.
    public static Uri Checked(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown)
        {
            throw Refused("The URL must be absolute, such as https://example.com/photo.jpg.");
        }

        return Checked(uri);
    }

    // The stream of the file at the URL. Nothing is sent until it is first read, so a caller whose upload the media
    // service refuses makes the server fetch nothing.
    public Stream Open(Uri url, CancellationToken cancellationToken) => new DownloadStream(this, url, cancellationToken);

    public void Dispose() => _client.Dispose();

    // Resolves, checks every address and connects to one of those it checked.
    internal static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);
        if (addresses.Length == 0 || !addresses.All(PublicAddresses.IsPublic))
        {
            throw Refused(NotPublic);
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

    private static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, cancellationToken),
    };

    private static Uri Checked(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw Refused("The URL must use https.");
        }

        if (uri.UserInfo.Length > 0)
        {
            throw Refused("The URL must not carry a user name or password.");
        }

        // A name is resolved and checked when the connection is made; an address is known now.
        return IPAddress.TryParse(uri.IdnHost, out var literal) && !PublicAddresses.IsPublic(literal) ? throw Refused(NotPublic) : uri;
    }

    private static McpDownloadException Refused(string reason) => new(McpErrorCodes.UploadSourceRefused, reason);

    private static McpDownloadException Failed(string reason) => new(McpErrorCodes.UploadSourceFailed, reason);

    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken cancellationToken)
    {
        for (var redirects = 0; ; redirects++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                response = await _client.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                // The handler wraps what the connection threw, which may be the refusal of an address.
                for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
                {
                    if (inner is McpDownloadException refused)
                    {
                        throw refused;
                    }
                }

                throw Failed("The URL could not be reached.");
            }

            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null || redirects == MaxRedirects)
                {
                    throw Refused(location is null ? "The URL redirects without saying where." : $"The URL redirects more than {MaxRedirects} times.");
                }

                url = Checked(location.IsAbsoluteUri ? location : new Uri(url, location));
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                response.Dispose();
                throw Failed($"The URL answered {status}.");
            }

            return response;
        }
    }

    // Reads the body of the response, which is asked for on the first read. The timeout runs from then and covers the
    // request and the body. The caller's own cancellation, which the deadline is linked to, is not a failure of the fetch.
    private sealed class DownloadStream(McpDownloads downloads, Uri url, CancellationToken caller) : Stream
    {
        private readonly CancellationTokenSource _deadline = CancellationTokenSource.CreateLinkedTokenSource(caller);
        private HttpResponseMessage? _response;
        private Stream? _body;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_body is null)
                {
                    _deadline.CancelAfter(downloads._timeout);
                    _response = await downloads.SendAsync(url, _deadline.Token);
                    _body = await _response.Content.ReadAsStreamAsync(_deadline.Token);
                }

                return await _body.ReadAsync(buffer, _deadline.Token);
            }
            catch (Exception exception) when (exception is not McpDownloadException && !cancellationToken.IsCancellationRequested && !caller.IsCancellationRequested)
            {
                throw exception is OperationCanceledException
                    ? Failed($"The URL did not answer within {downloads._timeout.TotalSeconds:0} seconds.")
                    : Failed("The file could not be read from the URL.");
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("The stream is read asynchronously.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _body?.Dispose();
                _response?.Dispose();
                _deadline.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

using FluentCMS.Core.Models;
using FluentCMS.Infrastructure.Models;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Webhooks;

// One client for every delivery. It sends only to allow-listed hosts and follows no redirect, so a redirect comes back as the
// endpoint's answer instead of leading somewhere the allow-list never saw (SEC-05).
internal sealed class WebhookClient(IOutboundHosts outbound, IOptions<WebhookOptions> options) : IDisposable
{
    private readonly HttpClient _http = new(new OutboundAllowListHandler(outbound)
    {
        InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
    })
    {
        Timeout = options.Value.Timeout,
    };

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _http.SendAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

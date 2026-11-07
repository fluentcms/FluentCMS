using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Webhooks;

internal sealed record WebhookResult(bool Succeeded, int? StatusCode, long DurationMs, string? Response, string? Error);

// Sends one attempt. A failure of the receiver is a result and never an exception; only the job's own cancellation throws.
internal sealed class WebhookSender : IDisposable
{
    public const string EventHeader = "X-FluentCMS-Event";
    public const string DeliveryHeader = "X-FluentCMS-Delivery";
    public const string SignatureHeader = "X-FluentCMS-Signature";

    private const int ResponseBytes = 4096;

    private readonly HttpClient _client;
    private readonly WebhooksOptions _options;
    private readonly bool _development;
    private readonly TimeProvider _time;

    public WebhookSender(IOptions<WebhooksOptions> options, IHostEnvironment environment, TimeProvider time)
    {
        _options = options.Value;
        _development = environment.IsDevelopment();
        _time = time;
        _client = new HttpClient(WebhookAddresses.CreateHandler(_options.AllowPrivateNetworks)) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FluentCMS-Webhooks", "1.0"));
    }

    // Signed with each of the secrets, the current one first.
    public async Task<WebhookResult> SendAsync(string url, IReadOnlyList<string> secrets, IReadOnlyDictionary<string, string> headers, WebhookAttempt attempt, CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        if (WebhookAddresses.UrlRefusal(url, _development) is { } refusal)
        {
            return new WebhookResult(false, null, 0, null, refusal);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(attempt.Body, Encoding.UTF8, "application/json") };
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            request.Headers.Add(EventHeader, attempt.Event);
            request.Headers.Add(DeliveryHeader, attempt.DeliveryId.ToString("D"));
            request.Headers.Add(SignatureHeader, WebhookSigning.Signature(_time.GetUtcNow().ToUnixTimeSeconds(), attempt.Body, secrets));

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var excerpt = await ExcerptAsync(response, timeout.Token);
            var status = (int)response.StatusCode;
            var error = response.IsSuccessStatusCode ? null
                : status is >= 300 and < 400 ? $"The receiver answered {status}; redirects are not followed."
                : $"The receiver answered {status}.";
            return new WebhookResult(response.IsSuccessStatusCode, status, Elapsed(started), excerpt, error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookResult(false, null, Elapsed(started), null, $"The receiver did not answer within {_options.Timeout.TotalSeconds:0.#} seconds.");
        }
        catch (HttpRequestException exception)
        {
            return new WebhookResult(false, null, Elapsed(started), null, Describe(exception));
        }
        catch (IOException exception)
        {
            return new WebhookResult(false, null, Elapsed(started), null, Cut($"The answer could not be read: {exception.Message}", WebhookModel.ErrorMaxLength));
        }
    }

    public void Dispose() => _client.Dispose();

    private long Elapsed(long started) => (long)_time.GetElapsedTime(started).TotalMilliseconds;

    private static string Describe(HttpRequestException exception)
    {
        for (Exception? inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is WebhookAddressException refused)
            {
                return refused.Message;
            }
        }

        return Cut($"The request failed: {exception.Message}", WebhookModel.ErrorMaxLength);
    }

    private static async Task<string?> ExcerptAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[ResponseBytes];
        var read = 0;
        while (read < buffer.Length && await stream.ReadAsync(buffer.AsMemory(read), cancellationToken) is var count and > 0)
        {
            read += count;
        }

        return read == 0 ? null : Cut(Encoding.UTF8.GetString(buffer, 0, read), WebhookModel.ResponseExcerptMaxLength);
    }

    private static string Cut(string text, int length) => text.Length <= length ? text : text[..length];
}

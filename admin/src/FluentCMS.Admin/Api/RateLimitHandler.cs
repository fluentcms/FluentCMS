using System.Net;

namespace FluentCMS.Admin.Api;

// Every tab of a user shares the principal's budget, so a 429 waits the Retry-After it carries and tries once more (spec
// 2.7). A second 429, one that says nothing of when to retry, or one that asks for more than MaxWait, is shown to the user
// and returned at once. A 429 is refused before the endpoint runs, so sending a write again is safe.
internal sealed class RateLimitHandler(TimeProvider time, IRateLimitNotice notice, TimeSpan? attemptTimeout = null) : DelegatingHandler
{
    // Beyond it the user is better told than kept waiting; the server's token bucket asks for 60 s / limit at most per request.
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    // Each attempt is held to its own time, as HttpClient's default holds one request, so one hung attempt cannot spend the
    // whole call's timeout.
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(100);

    private readonly TimeSpan _attemptTimeout = attemptTimeout ?? AttemptTimeout;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await AttemptAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return response;
        }

        if (RetryAfter(response) is { } wait && wait <= MaxWait)
        {
            response.Dispose();
            await Task.Delay(wait, time, cancellationToken);
            response = await AttemptAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return response;
            }
        }

        notice.TooManyRequests(RetryAfter(response));
        return response;
    }

    // A timeout is a TaskCanceledException with a TimeoutException inside, as HttpClient throws for its own.
    private async Task<HttpResponseMessage> AttemptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(_attemptTimeout);
        try
        {
            return await base.SendAsync(request, attempt.Token);
        }
        catch (OperationCanceledException canceled) when (!cancellationToken.IsCancellationRequested && attempt.IsCancellationRequested)
        {
            throw new TaskCanceledException(
                $"{request.Method} {request.RequestUri} was canceled after {_attemptTimeout.TotalSeconds} seconds.",
                new TimeoutException(canceled.Message, canceled));
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta < TimeSpan.Zero ? TimeSpan.Zero : delta,
        { Date: { } date } => date > time.GetUtcNow() ? date - time.GetUtcNow() : TimeSpan.Zero,
        _ => null,
    };
}

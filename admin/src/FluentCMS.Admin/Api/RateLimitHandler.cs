using System.Net;

namespace FluentCMS.Admin.Api;

// Every tab of a user shares the principal's budget, so a 429 waits the Retry-After it carries and tries once more (spec
// 2.7). A second 429, one that says nothing of when to retry, or one that asks for more than MaxWait, is shown to the user
// and returned at once. A 429 is refused before the endpoint runs, so sending a write again is safe.
internal sealed class RateLimitHandler(TimeProvider time, IRateLimitNotice notice) : DelegatingHandler
{
    // Beyond it the user is better told than kept waiting; the server's token bucket asks for 60 s / limit at most per request.
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return response;
        }

        if (RetryAfter(response) is { } wait && wait <= MaxWait)
        {
            response.Dispose();
            await Task.Delay(wait, time, cancellationToken);
            response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests)
            {
                return response;
            }
        }

        notice.TooManyRequests(RetryAfter(response));
        return response;
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta < TimeSpan.Zero ? TimeSpan.Zero : delta,
        { Date: { } date } => date > time.GetUtcNow() ? date - time.GetUtcNow() : TimeSpan.Zero,
        _ => null,
    };
}

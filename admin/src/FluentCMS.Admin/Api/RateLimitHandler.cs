using System.Net;

namespace FluentCMS.Admin.Api;

// Every tab of a user shares the principal's budget, so a 429 waits the Retry-After it carries and tries once more; a
// second 429, or one that says nothing of when to retry, is shown to the user and returned (spec 2.7). A 429 is refused
// before the endpoint runs, so sending a write again is safe.
internal sealed class RateLimitHandler(TimeProvider time, IRateLimitNotice notice) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return response;
        }

        if (RetryAfter(response) is { } wait)
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

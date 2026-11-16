namespace FluentCMS.Admin.Api;

// What the client sends until the sign-in, the shell and the notifications register their own: no token, the default site
// and locale, and no notice.
internal sealed class SignedOutSession : IAccessTokenSource, ICurrentSite, IContentLocale, IRateLimitNotice
{
    public string? Site => null;

    public string? Locale => null;

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);

    public void TooManyRequests(TimeSpan? retryAfter)
    {
    }
}

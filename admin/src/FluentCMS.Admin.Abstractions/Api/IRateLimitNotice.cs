namespace FluentCMS.Admin.Api;

/// <summary>
/// Tells the user that the API refused a request for too many requests after the client waited once and tried again. The
/// shell's notifications implement it; the request still ends with a 429 <see cref="ApiProblemException"/>.
/// </summary>
public interface IRateLimitNotice
{
    /// <summary>Shows the notice.</summary>
    /// <param name="retryAfter">How long the API asked to wait before the next request, if it said.</param>
    void TooManyRequests(TimeSpan? retryAfter);
}

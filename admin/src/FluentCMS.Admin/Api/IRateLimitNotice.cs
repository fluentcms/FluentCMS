namespace FluentCMS.Admin.Api;

// Tells the user that the API refused a request for too many requests, after one wait and retry or when the wait asked for
// is too long; the request still ends with a 429 ApiProblemException. It is called from inside the HTTP pipeline, so an
// implementation hands off to the renderer itself.
internal interface IRateLimitNotice
{
    void TooManyRequests(TimeSpan? retryAfter);
}

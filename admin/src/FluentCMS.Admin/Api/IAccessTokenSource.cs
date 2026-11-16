namespace FluentCMS.Admin.Api;

// The access token of the signed-in user, sent as Authorization: Bearer; the sign-in (A-07) fills it. It is asked on every
// send, the retry after a 429 included, so a refreshed token goes out then. Null sends no Authorization.
internal interface IAccessTokenSource
{
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}

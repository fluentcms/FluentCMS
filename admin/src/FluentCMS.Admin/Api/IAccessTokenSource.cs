namespace FluentCMS.Admin.Api;

// The access token of the signed-in user, sent as Authorization: Bearer; the sign-in fills it. It is asked on every send, the
// retry after a 429 and the resend after a 401 included, so a refreshed token goes out then. Null sends no Authorization.
internal interface IAccessTokenSource
{
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    // The server answered 401 to the token sent: true when a fresh token is there to send the request again with, either
    // refreshed now or by another call since.
    ValueTask<bool> RefreshAsync(string rejectedToken, CancellationToken cancellationToken);
}

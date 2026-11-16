using System.Net;

namespace FluentCMS.Admin.Api;

// A 401 to a request that carried a bearer token (FCMS3102) refreshes the session once and sends the same request again,
// which the headers below set the new token on. The session refreshes single flight, so many 401s at once refresh once. A
// second 401, or a refresh the server refused, is returned as it is; the session has ended then and goes to sign in.
internal sealed class TokenRefreshHandler(IAccessTokenSource tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized
            || request.Headers.Authorization is not { Scheme: "Bearer", Parameter: { Length: > 0 } sent }
            || !await tokens.RefreshAsync(sent, cancellationToken))
        {
            return response;
        }

        response.Dispose();
        return await base.SendAsync(request, cancellationToken);
    }
}

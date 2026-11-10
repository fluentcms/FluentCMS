using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Core.Identity.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Core.Identity.PreviewTokens;

internal static class PreviewTokenAuthentication
{
    public const string Scheme = "Preview";

    public static bool IsPresented(HttpRequest request) =>
        request.Headers.Authorization.Any(value => string.Equals(ApiKeyAuthentication.SchemeOf(value), Scheme, StringComparison.OrdinalIgnoreCase));
}

// Authorization: Preview <token>. Its own scheme rather than Bearer, so the bearer handler never reads it and a request says
// which kind of token it sends. A request with it and any other credential fails, as one with an API key does.
internal sealed class PreviewTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    PreviewTokenIssuer issuer)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!PreviewTokenAuthentication.IsPresented(Request))
        {
            return AuthenticateResult.NoResult();
        }

        var values = Request.Headers.Authorization;
        if (values.Count > 1)
        {
            return AuthenticateResult.Fail($"{ApiKeyErrorCodes.SeveralCredentials}: a request carries one credential, and this one carries a preview token and another.");
        }

        var token = values[0]!.TrimStart()[PreviewTokenAuthentication.Scheme.Length..].Trim();
        var (identity, failure) = await issuer.ValidateAsync(token, Scheme.Name);
        return failure switch
        {
            PreviewTokenFailure.Expired => AuthenticateResult.Fail($"{PreviewTokenErrorCodes.Expired}: the preview token has expired."),
            PreviewTokenFailure.None when identity is not null => AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)),
            _ => AuthenticateResult.Fail($"{PreviewTokenErrorCodes.InvalidToken}: the preview token is not valid."),
        };
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers[HeaderNames.WWWAuthenticate] = PreviewTokenAuthentication.Scheme;
        return base.HandleChallengeAsync(properties);
    }
}

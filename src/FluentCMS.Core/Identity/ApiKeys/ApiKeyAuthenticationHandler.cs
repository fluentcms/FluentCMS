using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Core.Identity.ApiKeys;

internal static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string Header = "X-Api-Key";
}

// Authorization: ApiKey <key>, or X-Api-Key: <key>. A request with neither is not this handler's, so another scheme may
// take it; one with a key that is wrong, expired or revoked fails. The failure names a code and never the key.
internal sealed class ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Presented() is not { } secret)
        {
            return AuthenticateResult.NoResult();
        }

        var keys = Context.RequestServices.GetRequiredService<ApiKeyService>();
        var check = await keys.AuthenticateAsync(secret, Context.RequestAborted);
        if (check.Key is not { } key)
        {
            return AuthenticateResult.Fail($"{check.Failure}: the API key is not valid, has expired or was revoked.");
        }

        var identity = new ClaimsIdentity(
            ApiKeyClaims.Of(key).Select(claim => new Claim(claim.Key, claim.Value)),
            Scheme.Name,
            CmsClaimTypes.Name,
            CmsClaimTypes.Roles);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers[HeaderNames.WWWAuthenticate] = ApiKeyAuthentication.Scheme;
        return base.HandleChallengeAsync(properties);
    }

    private string? Presented()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith(ApiKeyAuthentication.Scheme + " ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization[(ApiKeyAuthentication.Scheme.Length + 1)..].Trim();
        }

        var header = Request.Headers[ApiKeyAuthentication.Header].ToString().Trim();
        return header.Length > 0 ? header : null;
    }
}

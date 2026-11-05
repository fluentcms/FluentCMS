using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FluentCMS.Core.Identity.ApiKeys;

internal static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string Header = "X-Api-Key";

    public static bool IsPresented(HttpRequest request) =>
        request.Headers.ContainsKey(Header) || request.Headers.Authorization.Any(value => IsApiKeyScheme(SchemeOf(value)));

    // What an Authorization value starts with, up to its first whitespace of any kind.
    public static string SchemeOf(string? authorization) =>
        new([.. (authorization ?? "").TrimStart().TakeWhile(character => !char.IsWhiteSpace(character))]);

    private static bool IsApiKeyScheme(string scheme) => string.Equals(scheme, Scheme, StringComparison.OrdinalIgnoreCase);

    // Every credential the request carries: the API keys, and whether there is any other. Null when it carries no API key.
    public static (List<string> Keys, bool Other)? CredentialsOf(HttpRequest request)
    {
        List<string> keys = [];
        var other = false;
        foreach (var value in request.Headers.Authorization)
        {
            var scheme = SchemeOf(value);
            if (scheme.Length == 0)
            {
                continue;
            }

            if (IsApiKeyScheme(scheme))
            {
                keys.Add(value!.TrimStart()[scheme.Length..].Trim());
            }
            else
            {
                other = true;
            }
        }

        if (request.Headers.TryGetValue(Header, out var headers))
        {
            keys.AddRange(headers.Select(value => (value ?? "").Trim()));
        }

        return keys.Count == 0 ? null : (keys, other);
    }
}

// Authorization: ApiKey <key>, or X-Api-Key: <key>. A request with neither is not this handler's, so another scheme may
// take it; one with a key that is wrong, expired or revoked fails, and so does one with a key and any other credential,
// which would otherwise be read as whichever came first. The failure names a code and never the key.
internal sealed class ApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (ApiKeyAuthentication.CredentialsOf(Request) is not var (keys, other))
        {
            return AuthenticateResult.NoResult();
        }

        if (keys.Count > 1 || other)
        {
            return AuthenticateResult.Fail($"{ApiKeyErrorCodes.SeveralCredentials}: a request carries one credential, and this one carries an API key and another.");
        }

        var service = Context.RequestServices.GetRequiredService<ApiKeyService>();
        var check = await service.AuthenticateAsync(keys[0], Context.RequestAborted);
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
}

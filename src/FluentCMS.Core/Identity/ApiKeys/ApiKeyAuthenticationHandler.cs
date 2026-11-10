using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Telemetry;
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

    private static readonly object VerifiedItem = new();

    // The row of the key the handler verified for this request, so that what asks for the key's role before the request
    // ends does not read it again. Null for another key, and outside a request, as in a job.
    public static ApiKey? Verified(HttpContext? context, Guid id) =>
        context is not null && context.Items.TryGetValue(VerifiedItem, out var item) && item is ApiKey key && key.Id == id ? key : null;

    public static void Verify(HttpContext context, ApiKey key) => context.Items[VerifiedItem] = key;

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
// which would otherwise be read as whichever came first. The failure names a code and never the key, and a wrong, expired
// or revoked key gets the same one, so that the answer does not tell that a secret was ever a key. A valid key from an address
// it is not allowed from gets that code too, so a stolen key does not say that it would work from elsewhere.
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
            Refused(ApiKeyErrorCodes.SeveralCredentials, null);
            return AuthenticateResult.Fail($"{ApiKeyErrorCodes.SeveralCredentials}: a request carries one credential, and this one carries an API key and another.");
        }

        var service = Context.RequestServices.GetRequiredService<ApiKeyService>();
        var check = await service.AuthenticateAsync(keys[0], Context.Connection.RemoteIpAddress, Context.RequestAborted);
        if (check.Key is not { } key)
        {
            Refused(check.Failure!, check.RefusedKeyId);
            if (check is { Failure: ApiKeyErrorCodes.AddressRefused, Refused: { } refused })
            {
                // The request is refused either way; a failed write must not turn its 401 into a 500.
                try
                {
                    await service.RecordAddressRefusedAsync(refused, Context.Connection.RemoteIpAddress, System.Diagnostics.Activity.Current?.Id ?? Context.TraceIdentifier);
                }
                catch (Exception exception)
                {
                    ApiKeyLog.RefusalNotRecorded(Logger, exception, refused.Id);
                }
            }

            return AuthenticateResult.Fail($"{ApiKeyErrorCodes.InvalidKey}: the API key is not valid, has expired or was revoked.");
        }

        ApiKeyAuthentication.Verify(Context, key);
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

    // A refusal writes no activity row, except a valid key's from the wrong address: a row per refused key would let a flood
    // of keys write as fast as it is sent, and the limit before authentication already bounds it. The log and the metric
    // keep the reason, which the client is not told.
    private void Refused(string code, Guid? keyId)
    {
        ApiKeyLog.Refused(Logger, code, Context.Connection.RemoteIpAddress, keyId);
        FluentCmsTelemetry.ApiKeysRefused.Add(1, new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Code, code));
    }
}

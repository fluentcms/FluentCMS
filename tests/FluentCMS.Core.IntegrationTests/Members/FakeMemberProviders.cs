using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.IntegrationTests.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace FluentCMS.Core.IntegrationTests.Members;

// Google, Microsoft, GitHub and Apple in the process, with no network: the handlers' back channel reaches their token,
// user information and metadata endpoints through Handler, and a test plays the browser's visit to the authorize
// endpoint with Authorize. A code works once, PKCE is checked where the handler sends it, and the secrets and the Apple
// key are made for the run. A request for anything else is answered 404 and kept in Unknown, so a test can tell that
// nothing was asked of a real provider.
internal sealed class FakeMemberProviders : IDisposable
{
    public const string GoogleClientId = "google-client";
    public const string MicrosoftClientId = "microsoft-client";
    public const string GitHubClientId = "github-client";
    public const string AppleClientId = "com.example.members";
    public const string AppleTeamId = "TEAM123456";
    public const string AppleKeyId = "KEY1234567";

    private const string AppleIssuer = "https://appleid.apple.com";

    private static readonly Dictionary<string, (string Kind, string ClientId)> AuthorizeEndpoints = new(StringComparer.Ordinal)
    {
        ["https://accounts.google.com/o/oauth2/v2/auth"] = ("google", GoogleClientId),
        ["https://login.microsoftonline.com/common/oauth2/v2.0/authorize"] = ("microsoft", MicrosoftClientId),
        ["https://github.com/login/oauth/authorize"] = ("github", GitHubClientId),
        ["https://appleid.apple.com/auth/authorize"] = ("apple", AppleClientId),
    };

    private static readonly Dictionary<string, string> TokenEndpoints = new(StringComparer.Ordinal)
    {
        ["https://oauth2.googleapis.com/token"] = "google",
        ["https://login.microsoftonline.com/common/oauth2/v2.0/token"] = "microsoft",
        ["https://github.com/login/oauth/access_token"] = "github",
        ["https://appleid.apple.com/auth/token"] = "apple",
    };

    private readonly ECDsa _appleClientKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly RSA _appleSigningKey = RSA.Create(2048);
    private readonly ConcurrentDictionary<string, Grant> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Grant> _accessTokens = new(StringComparer.Ordinal);

    public FakeMemberProviders()
    {
        Handler = new Endpoints(this);
    }

    public HttpMessageHandler Handler { get; }

    // The client secret of the Google, Microsoft and GitHub providers.
    public string ClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    // The Sign in with Apple key of the run, as the .p8 file holds it.
    public string ApplePrivateKey => _appleClientKey.ExportPkcs8PrivateKeyPem();

    public ConcurrentQueue<TokenRequest> TokenRequests { get; } = new();

    public ConcurrentQueue<string> Unknown { get; } = new();

    // What the browser brings back to the callback after the visitor signed in at the provider: the code and the state,
    // or the provider's error.
    public Dictionary<string, string> Authorize(Uri authorizeUrl, FakeAccount account, string? error = null)
    {
        AuthorizeEndpoints.TryGetValue(authorizeUrl.GetLeftPart(UriPartial.Path), out var provider).ShouldBeTrue(authorizeUrl.AbsoluteUri);
        var query = QueryHelpers.ParseQuery(authorizeUrl.Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        query["client_id"].ShouldBe(provider.ClientId);
        query["response_type"].ShouldBe("code");
        query["state"].ShouldNotBeNullOrEmpty();
        if (provider.Kind == "apple")
        {
            query["response_mode"].ShouldBe("form_post");
        }

        if (error is not null)
        {
            return new() { ["error"] = error, ["state"] = query["state"] };
        }

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _codes[code] = new Grant(provider.Kind, account, query["redirect_uri"], query.GetValueOrDefault("code_challenge"), query.GetValueOrDefault("scope") ?? "");
        return new() { ["code"] = code, ["state"] = query["state"] };
    }

    public void Dispose()
    {
        Handler.Dispose();
        _appleClientKey.Dispose();
        _appleSigningKey.Dispose();
    }

    private async Task<HttpResponseMessage> TokenAsync(string kind, HttpRequestMessage request)
    {
        var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync()).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        TokenRequests.Enqueue(new TokenRequest(kind, form.GetValueOrDefault("redirect_uri"), form.GetValueOrDefault("client_secret")));

        if (form.GetValueOrDefault("grant_type") != "authorization_code"
            || !_codes.TryRemove(form.GetValueOrDefault("code") ?? "", out var grant)
            || grant.Kind != kind
            || form.GetValueOrDefault("redirect_uri") != grant.RedirectUri
            || (grant.Challenge is not null && Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(form.GetValueOrDefault("code_verifier") ?? ""))) != grant.Challenge)
            || !(kind == "apple" ? await IsAppleClientSecretAsync(form.GetValueOrDefault("client_secret")) : form.GetValueOrDefault("client_secret") == ClientSecret))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };
        }

        var accessToken = "at-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _accessTokens[accessToken] = grant;
        return kind == "apple"
            ? Json(new { access_token = accessToken, token_type = "Bearer", expires_in = 300, id_token = AppleIdToken(grant.Account) })
            : Json(new { access_token = accessToken, token_type = "Bearer", expires_in = 300, scope = grant.Scope });
    }

    // The client secret the Apple handler makes: a token signed with the key of the run, in the name of the team.
    private async Task<bool> IsAppleClientSecretAsync(string? secret)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(secret, new TokenValidationParameters
        {
            ValidIssuer = AppleTeamId,
            ValidAudience = AppleIssuer,
            IssuerSigningKey = new ECDsaSecurityKey(_appleClientKey) { KeyId = AppleKeyId },
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        });
        return result.IsValid && result.SecurityToken is JsonWebToken { Subject: AppleClientId, Kid: AppleKeyId };
    }

    private string AppleIdToken(FakeAccount account)
    {
        var now = DateTime.UtcNow;
        Dictionary<string, object> claims = new() { ["sub"] = account.Subject };
        if (account.Email is not null)
        {
            claims["email"] = account.Email;
        }

        // Apple sends the flag as text.
        if (account.EmailVerified is { } verified)
        {
            claims["email_verified"] = verified ? "true" : "false";
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AppleIssuer,
            Audience = AppleClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_appleSigningKey) { KeyId = "apple-key" }, SecurityAlgorithms.RsaSha256),
        });
    }

    private object AppleKeys()
    {
        var parameters = _appleSigningKey.ExportParameters(includePrivateParameters: false);
        return new
        {
            keys = new[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = "apple-key", n = Base64Url.EncodeToString(parameters.Modulus), e = Base64Url.EncodeToString(parameters.Exponent) },
            },
        };
    }

    private HttpResponseMessage UserInformation(string url, HttpRequestMessage request)
    {
        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: { } accessToken } || !_accessTokens.TryGetValue(accessToken, out var grant))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        var account = grant.Account;
        return (url, grant.Kind) switch
        {
            ("https://www.googleapis.com/oauth2/v3/userinfo", "google") => Json(new Dictionary<string, object?>
            {
                ["sub"] = account.Subject,
                ["name"] = account.Name,
                ["email"] = account.Email,
                ["email_verified"] = account.EmailVerified,
            }.Where(pair => pair.Value is not null).ToDictionary()),
            ("https://graph.microsoft.com/v1.0/me", "microsoft") => Json(new { id = account.Subject, displayName = account.Name, mail = account.Email, userPrincipalName = account.Email }),
            ("https://api.github.com/user", "github") => Json(new { id = long.Parse(account.Subject, System.Globalization.CultureInfo.InvariantCulture), login = account.Login, name = account.Name, email = account.PublicEmail }),
            ("https://api.github.com/user/emails", "github") => grant.Scope.Contains("user:email", StringComparison.Ordinal)
                ? Json(account.Addresses.Select(address => new { email = address.Email, primary = address.Primary, verified = address.Verified }))
                : new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden),
        };
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private sealed record Grant(string Kind, FakeAccount Account, string RedirectUri, string? Challenge, string Scope);

    private sealed class Endpoints(FakeMemberProviders providers) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
            if (request.Method == HttpMethod.Post && TokenEndpoints.TryGetValue(url, out var kind))
            {
                return await providers.TokenAsync(kind, request);
            }

            if (request.Method == HttpMethod.Get)
            {
                switch (url)
                {
                    case AppleIssuer + "/.well-known/openid-configuration":
                        return Json(new
                        {
                            issuer = AppleIssuer,
                            authorization_endpoint = AppleIssuer + "/auth/authorize",
                            token_endpoint = AppleIssuer + "/auth/token",
                            jwks_uri = AppleIssuer + "/auth/keys",
                        });
                    case AppleIssuer + "/auth/keys":
                        return Json(providers.AppleKeys());
                    case "https://www.googleapis.com/oauth2/v3/userinfo" or "https://graph.microsoft.com/v1.0/me" or "https://api.github.com/user" or "https://api.github.com/user/emails":
                        return providers.UserInformation(url, request);
                }
            }

            providers.Unknown.Enqueue($"{request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}

internal sealed record TokenRequest(string Kind, string? RedirectUri, string? ClientSecret);

// An account at a provider. EmailVerified is what Google and Apple say of the address, and null leaves the flag out;
// Microsoft has no such flag. GitHub has a public address in the profile and a list of addresses, each primary or not
// and verified or not.
internal sealed record FakeAccount(string Subject, string? Email)
{
    public string? Name { get; init; } = "Pat Visitor";

    public bool? EmailVerified { get; init; } = true;

    public string Login { get; init; } = "pat-visitor";

    public string? PublicEmail { get; init; }

    public IReadOnlyList<(string Email, bool Primary, bool Verified)> Addresses { get; init; } = [];

    public static FakeAccount Of(string? email) => new(RandomNumberGenerator.GetInt32(1_000_000, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture), email);

    public FakeIdentity ToIdentity() => new(Subject, Email, [], ["pwd"]) { EmailVerified = EmailVerified };
}

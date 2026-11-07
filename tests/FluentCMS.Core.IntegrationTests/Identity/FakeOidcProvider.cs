using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace FluentCMS.Core.IntegrationTests.Identity;

// An OpenID Connect provider in the process, with no network: the handler's back channel reaches its discovery document,
// key set and token endpoint through Handler, and a test plays the browser's visit to its authorize endpoint with
// Authorize. It issues codes once, checks PKCE, and signs id tokens with an RSA key of its own.
internal sealed class FakeOidcProvider : IDisposable
{
    public const string ClientId = "fluentcms";
    public const string ClientSecret = "fake-provider-secret";

    private readonly RSA _key = RSA.Create(2048);
    private readonly ConcurrentDictionary<string, Grant> _grants = new(StringComparer.Ordinal);

    public FakeOidcProvider(string issuer = "https://idp.test")
    {
        Issuer = issuer.TrimEnd('/');
        Handler = new Endpoints(this);
    }

    public string Issuer { get; }

    public HttpMessageHandler Handler { get; }

    public int TokenRequests { get; private set; }

    // What the browser brings back to the callback after the user signed in at the provider: the code and the state, or
    // the provider's error.
    public Dictionary<string, string> Authorize(Uri authorizeUrl, FakeIdentity user, string? error = null)
    {
        authorizeUrl.GetLeftPart(UriPartial.Path).ShouldBe(Issuer + "/authorize");
        var query = QueryHelpers.ParseQuery(authorizeUrl.Query).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        query["client_id"].ShouldBe(ClientId);
        query["response_type"].ShouldBe("code");
        query["code_challenge_method"].ShouldBe("S256");
        query["scope"].Split(' ').ShouldContain("openid");

        if (error is not null)
        {
            return new() { ["error"] = error, ["state"] = query["state"] };
        }

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _grants[code] = new Grant(user, query["nonce"], query["code_challenge"], query["redirect_uri"]);
        return new() { ["code"] = code, ["state"] = query["state"] };
    }

    public void Dispose()
    {
        Handler.Dispose();
        _key.Dispose();
    }

    private string IdToken(Grant grant)
    {
        var now = DateTime.UtcNow;
        Dictionary<string, object> claims = new()
        {
            ["sub"] = grant.User.Subject,
            ["nonce"] = grant.Nonce,
            ["name"] = grant.User.Name,
        };
        if (grant.User.Email is not null)
        {
            claims["email"] = grant.User.Email;
        }

        if (grant.User.EmailVerified is { } verified)
        {
            claims["email_verified"] = verified;
        }

        if (grant.User.Groups.Length > 0)
        {
            claims["groups"] = grant.User.Groups;
        }

        if (grant.User.Amr.Length > 0)
        {
            claims["amr"] = grant.User.Amr;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_key) { KeyId = "fake-key" }, SecurityAlgorithms.RsaSha256),
        });
    }

    private Dictionary<string, object> Discovery() => new()
    {
        ["issuer"] = Issuer,
        ["authorization_endpoint"] = Issuer + "/authorize",
        ["token_endpoint"] = Issuer + "/token",
        ["jwks_uri"] = Issuer + "/jwks",
        ["response_types_supported"] = new[] { "code" },
        ["subject_types_supported"] = new[] { "public" },
        ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
        ["code_challenge_methods_supported"] = new[] { "S256" },
    };

    private object KeySet()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        return new
        {
            keys = new[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = "fake-key", n = Base64Url.EncodeToString(parameters.Modulus), e = Base64Url.EncodeToString(parameters.Exponent) },
            },
        };
    }

    private async Task<HttpResponseMessage> TokenAsync(HttpRequestMessage request)
    {
        TokenRequests++;
        var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync()).ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        var secret = form.GetValueOrDefault("client_secret")
            ?? (request.Headers.Authorization is { Scheme: "Basic" } basic ? Encoding.UTF8.GetString(Convert.FromBase64String(basic.Parameter!)).Split(':')[1] : null);
        if (form.GetValueOrDefault("grant_type") != "authorization_code" || secret != ClientSecret
            || !_grants.TryRemove(form.GetValueOrDefault("code") ?? "", out var grant)
            || form.GetValueOrDefault("redirect_uri") != grant.RedirectUri
            || Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(form.GetValueOrDefault("code_verifier") ?? ""))) != grant.Challenge)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };
        }

        return Json(new { access_token = "fake-access-token", token_type = "Bearer", expires_in = 300, id_token = IdToken(grant) });
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private sealed record Grant(FakeIdentity User, string Nonce, string Challenge, string RedirectUri);

    private sealed class Endpoints(FakeOidcProvider provider) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.GetLeftPart(UriPartial.Path);
            if (path == provider.Issuer + "/.well-known/openid-configuration")
            {
                return Json(provider.Discovery());
            }

            if (path == provider.Issuer + "/jwks")
            {
                return Json(provider.KeySet());
            }

            if (path == provider.Issuer + "/token" && request.Method == HttpMethod.Post)
            {
                return await provider.TokenAsync(request);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}

internal sealed record FakeIdentity(string Subject, string? Email, string[] Groups, string[] Amr)
{
    public string Name => Email ?? Subject;

    // As a provider that verifies addresses says; null leaves the claim out.
    public bool? EmailVerified { get; init; } = true;

    public static FakeIdentity Of(string? email, params string[] groups) => new("sub-" + Guid.NewGuid().ToString("N"), email, groups, ["pwd"]);
}

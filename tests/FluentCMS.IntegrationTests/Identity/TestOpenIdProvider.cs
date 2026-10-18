using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.IntegrationTests.Identity;

// The person the provider says is signing in. A null EmailVerified leaves the claim out, as Microsoft Entra does.
internal sealed record ProviderIdentity(string Subject, string? Email, bool? EmailVerified = true);

// Just enough of an OpenID Connect provider for the code flow with PKCE and form_post: discovery, keys, authorize and token.
internal sealed class TestOpenIdProvider : IAsyncDisposable
{
    public const string ClientId = "fluentcms-admin";
    public const string ClientSecret = "test-client-secret";
    private const string KeyId = "test-key";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly ConcurrentDictionary<string, Grant> _grants = new();
    private readonly WebApplication _app;

    private TestOpenIdProvider(WebApplication app)
    {
        _app = app;
        app.MapGet("/.well-known/openid-configuration", Discovery);
        app.MapGet("/jwks", Keys);
        app.MapGet("/authorize", Authorize);
        app.MapPost("/token", (Delegate)TokenAsync);
    }

    public ProviderIdentity Identity { get; set; } = new("subject-1", "owner@example.com");

    public string Authority => _app.Urls.Single();

    public static async Task<TestOpenIdProvider> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var provider = new TestOpenIdProvider(builder.Build());
        await provider._app.StartAsync();
        return provider;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _rsa.Dispose();
    }

    private static string Issuer(HttpContext context) => $"{context.Request.Scheme}://{context.Request.Host}";

    private static IResult Discovery(HttpContext context)
    {
        var issuer = Issuer(context);
        return Results.Json(new Dictionary<string, object>
        {
            ["issuer"] = issuer,
            ["authorization_endpoint"] = issuer + "/authorize",
            ["token_endpoint"] = issuer + "/token",
            ["jwks_uri"] = issuer + "/jwks",
            ["response_types_supported"] = new[] { "code" },
            ["response_modes_supported"] = new[] { "form_post" },
            ["subject_types_supported"] = new[] { "public" },
            ["id_token_signing_alg_values_supported"] = new[] { SecurityAlgorithms.RsaSha256 },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_post" },
        });
    }

    private IResult Keys()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        return Results.Json(new Dictionary<string, object>
        {
            ["keys"] = new[]
            {
                new Dictionary<string, string>
                {
                    ["kty"] = "RSA",
                    ["use"] = "sig",
                    ["alg"] = SecurityAlgorithms.RsaSha256,
                    ["kid"] = KeyId,
                    ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                    ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
                },
            },
        });
    }

    // Signs the current identity in at once and answers with the form a browser would post back to the client.
    private IResult Authorize(HttpContext context)
    {
        var query = context.Request.Query;
        if (query["client_id"] != ClientId || query["response_type"] != "code" || query["code_challenge_method"] != "S256")
        {
            return Results.BadRequest();
        }

        var code = Guid.NewGuid().ToString("N");
        _grants[code] = new Grant(Identity, query["nonce"].ToString(), query["code_challenge"].ToString(), query["redirect_uri"].ToString());

        var html = $"""
            <form method="post" action="{WebUtility.HtmlEncode(query["redirect_uri"])}">
              <input type="hidden" name="code" value="{code}" />
              <input type="hidden" name="state" value="{WebUtility.HtmlEncode(query["state"])}" />
            </form>
            """;
        return Results.Content(html, "text/html");
    }

    private async Task<IResult> TokenAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync();
        if (form["grant_type"] != "authorization_code"
            || form["client_id"] != ClientId
            || form["client_secret"] != ClientSecret
            || !_grants.TryRemove(form["code"].ToString(), out var grant)
            || form["redirect_uri"] != grant.RedirectUri
            || Sha256Base64Url(form["code_verifier"].ToString()) != grant.CodeChallenge)
        {
            return Results.Json(new Dictionary<string, string> { ["error"] = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var accessToken = Guid.NewGuid().ToString("N");
        return Results.Json(new Dictionary<string, object>
        {
            ["access_token"] = accessToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = 300,
            ["id_token"] = IdToken(Issuer(context), grant, accessToken),
        });
    }

    private string IdToken(string issuer, Grant grant, string accessToken)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = grant.Identity.Subject,
            ["nonce"] = grant.Nonce,
            ["at_hash"] = AtHash(accessToken),
        };
        if (grant.Identity.Email is { } email)
        {
            claims["email"] = email;
        }

        if (grant.Identity.EmailVerified is { } verified)
        {
            claims["email_verified"] = verified;
        }

        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = ClientId,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
        });
    }

    // The left half of the SHA-256 of the access token (OpenID Connect Core 3.1.3.6).
    private static string AtHash(string accessToken)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        return Base64UrlEncoder.Encode(hash[..(hash.Length / 2)]);
    }

    private static string Sha256Base64Url(string value)
    {
        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    }

    private sealed record Grant(ProviderIdentity Identity, string Nonce, string CodeChallenge, string RedirectUri);
}

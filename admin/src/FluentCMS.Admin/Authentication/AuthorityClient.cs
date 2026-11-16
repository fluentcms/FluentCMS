using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentCMS.Admin.Configuration;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace FluentCMS.Admin.Authentication;

internal sealed class AuthorityOptions
{
    // Each request to the authorization server: well under HttpClient's 100 seconds, so a server that does not answer shows
    // the gate's failure with "Try again" soon.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    // The handler under the client: the browser's fetch when null, a fake or the test server's in the tests.
    internal Func<HttpMessageHandler>? PrimaryHandler { get; set; }

    internal TimeSpan Timeout { get; set; } = DefaultTimeout;
}

// The token endpoint refused a grant with an OAuth error, such as invalid_grant for a used or revoked refresh token. The
// message names the error only, never a token.
internal sealed class TokenRefusedException(string error) : Exception($"The token endpoint refused the grant: {error}.")
{
    public string Error { get; } = error;
}

// The endpoints of the authorization server the configuration names (spec 2.4): its discovery document, read once, and the
// token and revocation endpoints it lists. The browser sends these requests without cookies; none needs one.
internal sealed class AuthorityClient(AdminConfiguration configuration, HttpClient http) : IDisposable
{
    private const string DiscoveryPath = ".well-known/openid-configuration";

    private static AuthenticationJsonContext Json => AuthenticationJsonContext.Default;

    private readonly Lock _gate = new();
    private Task<OpenIdConfiguration>? _discovery;

    // A failed read is not kept, so the next sign-in reads the document again.
    public Task<OpenIdConfiguration> DiscoverAsync()
    {
        lock (_gate)
        {
            if (_discovery is null or { IsFaulted: true } or { IsCanceled: true })
            {
                _discovery = ReadDiscoveryAsync();
            }

            return _discovery;
        }
    }

    public async Task<TokenAnswer> RedeemCodeAsync(string code, string verifier, string redirectUri, CancellationToken cancellationToken) =>
        await TokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = configuration.ClientId,
                ["code_verifier"] = verifier,
            },
            cancellationToken);

    public async Task<TokenAnswer> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        await TokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = configuration.ClientId,
            },
            cancellationToken);

    // RFC 7009: the server answers 200 for a token it does not know, so anything else is a failure.
    public async Task RevokeAsync(Uri endpoint, string refreshToken, CancellationToken cancellationToken)
    {
        using var request = Post(endpoint, new Dictionary<string, string>
        {
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = configuration.ClientId,
        });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The revocation endpoint answered {(int)response.StatusCode}.", null, response.StatusCode);
        }
    }

    public void Dispose() => http.Dispose();

    // The issuer is compared with the configured authority as a string, ordinal, after one trailing slash is taken off each:
    // OpenIddict writes the issuer of a host without a path base with a slash ("https://cms.example.com/"), and config.json
    // usually without. Nothing else is folded, neither case nor a default port. Every endpoint is on the issuer's origin.
    internal static OpenIdConfiguration Validate(OpenIdConfiguration document, string authority)
    {
        if (document.Issuer is not { } issuer
            || !string.Equals(TrimOneSlash(issuer), TrimOneSlash(authority), StringComparison.Ordinal)
            || !Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri))
        {
            throw new InvalidOperationException($"The discovery document of {authority} names the issuer '{document.Issuer}', which is not the configured authority.");
        }

        var origin = issuerUri.GetLeftPart(UriPartial.Authority);
        Require(document.AuthorizationEndpoint, "authorization_endpoint", origin, required: true);
        Require(document.TokenEndpoint, "token_endpoint", origin, required: true);
        Require(document.RevocationEndpoint, "revocation_endpoint", origin, required: false);
        Require(document.EndSessionEndpoint, "end_session_endpoint", origin, required: false);
        if (document.CodeChallengeMethodsSupported is { } methods && !methods.Contains("S256", StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"The authorization server of {authority} does not support PKCE with S256.");
        }

        return document;
    }

    private static string TrimOneSlash(string value) => value.EndsWith('/') ? value[..^1] : value;

    private static void Require(Uri? endpoint, string member, string origin, bool required)
    {
        if (endpoint is null)
        {
            if (required)
            {
                throw new InvalidOperationException($"The discovery document has no {member}.");
            }

            return;
        }

        if (!endpoint.IsAbsoluteUri || !string.Equals(endpoint.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {member} of the discovery document is not on the issuer's origin {origin}.");
        }
    }

    private async Task<OpenIdConfiguration> ReadDiscoveryAsync()
    {
        var authority = configuration.Authority.OriginalString;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(TrimOneSlash(authority) + "/" + DiscoveryPath, UriKind.Absolute));
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Omit);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The discovery document of {authority} answered {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var document = await response.Content.ReadFromJsonAsync(Json.OpenIdConfiguration)
            ?? throw new JsonException($"The discovery document of {authority} is empty.");
        return Validate(document, authority);
    }

    private async Task<TokenAnswer> TokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var endpoint = (await DiscoverAsync()).TokenEndpoint!;
        using var request = Post(endpoint, form);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw new TokenRefusedException(error ?? "invalid_request");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The token endpoint answered {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var answer = await response.Content.ReadFromJsonAsync(Json.TokenAnswer, cancellationToken);
        if (answer is not { AccessToken: { Length: > 0 } } || !string.Equals(answer.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new TokenRefusedException("invalid_token_response");
        }

        return answer;
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync(Json.TokenError, cancellationToken))?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpRequestMessage Post(Uri endpoint, Dictionary<string, string> form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Omit);
        return request;
    }
}

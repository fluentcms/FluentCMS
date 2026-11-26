using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.IntegrationTests.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Shouldly;

namespace FluentCMS.Core.IntegrationTests.Members;

// Google, Microsoft and GitHub in the process, with no network: the handlers' back channel reaches their token and
// user information endpoints through Handler, and a test plays the browser's visit to the authorize endpoint with
// Authorize. A code works once, PKCE is checked where the handler sends it, and the secret is made for the run. A request for anything else is answered 404 and kept in Unknown, so a test can tell that
// nothing was asked of a real provider.
internal sealed class FakeMemberProviders : IDisposable
{
    public const string GoogleClientId = "google-client";
    public const string MicrosoftClientId = "microsoft-client";
    public const string GitHubClientId = "github-client";

    private static readonly Dictionary<string, (string Kind, string ClientId)> AuthorizeEndpoints = new(StringComparer.Ordinal)
    {
        ["https://accounts.google.com/o/oauth2/v2/auth"] = ("google", GoogleClientId),
        ["https://login.microsoftonline.com/common/oauth2/v2.0/authorize"] = ("microsoft", MicrosoftClientId),
        ["https://github.com/login/oauth/authorize"] = ("github", GitHubClientId),
    };

    private static readonly Dictionary<string, string> TokenEndpoints = new(StringComparer.Ordinal)
    {
        ["https://oauth2.googleapis.com/token"] = "google",
        ["https://login.microsoftonline.com/common/oauth2/v2.0/token"] = "microsoft",
        ["https://github.com/login/oauth/access_token"] = "github",
    };

    private readonly ConcurrentDictionary<string, Grant> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Grant> _accessTokens = new(StringComparer.Ordinal);

    public FakeMemberProviders()
    {
        Handler = new Endpoints(this);
    }

    public HttpMessageHandler Handler { get; }

    // The client secret of the Google, Microsoft and GitHub providers.
    public string ClientSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

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

        if (error is not null)
        {
            // Google sends the state back with a cancelled consent (verified 15 Nov 2026).
            return new() { ["error"] = error, ["state"] = query["state"] };
        }

        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var scope = query.GetValueOrDefault("scope") ?? "";
        _codes[code] = new Grant(provider.Kind, account, query["redirect_uri"], query.GetValueOrDefault("code_challenge"), scope);
        Dictionary<string, string> answer = new() { ["code"] = code, ["state"] = query["state"] };

        // What the real callbacks carry besides the code and the state (verified 15 Nov 2026): Google names its issuer,
        // the scopes it granted in their long form, the account and the prompt; GitHub names its issuer.
        switch (provider.Kind)
        {
            case "google":
                answer["iss"] = "https://accounts.google.com";
                answer["scope"] = GoogleGrantedScope(scope);
                answer["authuser"] = "0";
                answer["prompt"] = "consent";
                break;
            case "github":
                answer["iss"] = "https://github.com/login/oauth";
                break;
        }

        return answer;
    }

    // Google answers the short scopes it was asked for with their long names, in its own order.
    private static string GoogleGrantedScope(string asked)
    {
        var names = asked.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        List<string> granted = [];
        if (names.Remove("email"))
        {
            granted.Add("https://www.googleapis.com/auth/userinfo.email");
        }

        if (names.Remove("profile"))
        {
            granted.Add("https://www.googleapis.com/auth/userinfo.profile");
        }

        granted.AddRange(names);
        return string.Join(' ', granted);
    }

    public void Dispose()
    {
        Handler.Dispose();
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
            || form.GetValueOrDefault("client_secret") != ClientSecret)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };
        }

        var accessToken = "at-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _accessTokens[accessToken] = grant;
        return Json(new { access_token = accessToken, token_type = "Bearer", expires_in = 300, scope = kind == "google" ? GoogleGrantedScope(grant.Scope) : grant.Scope });
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

            if (request.Method == HttpMethod.Get
                && url is "https://www.googleapis.com/oauth2/v3/userinfo" or "https://graph.microsoft.com/v1.0/me" or "https://api.github.com/user" or "https://api.github.com/user/emails")
            {
                return providers.UserInformation(url, request);
            }

            providers.Unknown.Enqueue($"{request.Method} {request.RequestUri}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}

internal sealed record TokenRequest(string Kind, string? RedirectUri, string? ClientSecret);

// An account at a provider. EmailVerified is what Google and an OIDC provider say of the address, and null leaves the
// flag out; Microsoft has no such flag. GitHub has a public address in the profile and a list of addresses, each primary
// or not and verified or not.
internal sealed record FakeAccount(string Subject, string? Email)
{
    public string? Name { get; init; } = "Pat Visitor";

    public bool? EmailVerified { get; init; } = true;

    public bool EmailVerifiedAsText { get; init; }

    public string Login { get; init; } = "pat-visitor";

    public string? PublicEmail { get; init; }

    public IReadOnlyList<(string Email, bool Primary, bool Verified)> Addresses { get; init; } = [];

    public static FakeAccount Of(string? email) => new(RandomNumberGenerator.GetInt32(1_000_000, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture), email);

    public FakeIdentity ToIdentity() => new(Subject, Email, [], ["pwd"]) { EmailVerified = EmailVerified, EmailVerifiedAsText = EmailVerifiedAsText };
}

using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.Testing;

// What a browser does with the authorization server: the hosted pages (#2779) sign the user in with the Identity cookie,
// which is made here instead, and the client runs the authorization code flow with PKCE against the real endpoints.
internal static class OAuthFlows
{
    public const string ClientId = "fluentcms-testing";
    public const string RedirectUri = "https://localhost/signed-in";

    // The authorization server answers HTTPS only, so the clients of a user speak it.
    public static readonly Uri BaseAddress = new("https://localhost/");

    public static ClientRegistration Client() => new()
    {
        DisplayName = "FluentCMS tests",
        RedirectUris = { RedirectUri },
        Scopes = { "openid", "profile", "email", "offline_access", "draft", "management", "mcp" },
    };

    public static async Task<string> CookieAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var user = await provider.GetRequiredService<CmsUserManager>().FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"There is no user {userId}.");
        var principal = await provider.GetRequiredService<SignInManager<CmsUser>>().CreateUserPrincipalAsync(user);

        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var now = provider.GetRequiredService<TimeProvider>().GetUtcNow();
        var properties = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + options.ExpireTimeSpan };
        properties.Items[CmsSignInManager.SignedInAtItem] = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var ticket = new AuthenticationTicket(principal, properties, IdentityConstants.ApplicationScheme);
        return $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}";
    }

    public static async Task<TestTokens> SignInAsync(HttpClient client, IServiceProvider services, Guid userId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = string.Join(' ', scopes),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = "test",
        });

        using var authorize = new HttpRequestMessage(HttpMethod.Get, "/connect/authorize" + query);
        authorize.Headers.Add("Cookie", await CookieAsync(services, userId));
        using var response = await client.SendAsync(authorize, cancellationToken);
        var location = response.Headers.Location;
        if (response.StatusCode != HttpStatusCode.Redirect || location is null || !location.ToString().StartsWith(RedirectUri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The authorize endpoint answered {(int)response.StatusCode} {location}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        var parameters = QueryHelpers.ParseQuery(location.Query);
        if (!parameters.TryGetValue("code", out var code))
        {
            throw new InvalidOperationException($"The authorize endpoint returned no code: {location}");
        }

        return await TokenAsync(
            client,
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code.ToString(),
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = verifier,
            },
            cancellationToken);
    }

    public static async Task<TestTokens> TokenAsync(HttpClient client, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync("/connect/token", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The token endpoint answered {(int)response.StatusCode}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        return new TestTokens(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            root.TryGetProperty("id_token", out var identity) ? identity.GetString() : null);
    }
}

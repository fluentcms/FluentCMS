using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using AspNet.Security.OAuth.Apple;
using AspNet.Security.OAuth.GitHub;
using FluentCMS.Core.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity.Members;

// The OAuth handlers of the member providers. An OAuth handler builds the redirect_uri from the request, for the
// provider and again when it redeems the code, and has no event between the two; these build it from the public origin
// when one is set (spec 4.15), as the OpenID Connect providers do through their event.
internal sealed class MemberGoogleHandler(IOptionsMonitor<GoogleOptions> options, ILoggerFactory logger, UrlEncoder encoder, PublicOrigin origin)
    : GoogleHandler(options, logger, encoder)
{
    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri) =>
        base.BuildChallengeUrl(properties, MemberProviders.Callback(origin, Options.CallbackPath, redirectUri));

    protected override Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context) =>
        base.ExchangeCodeAsync(MemberProviders.Callback(origin, Options.CallbackPath, context));
}

internal sealed class MemberMicrosoftHandler(IOptionsMonitor<MicrosoftAccountOptions> options, ILoggerFactory logger, UrlEncoder encoder, PublicOrigin origin)
    : MicrosoftAccountHandler(options, logger, encoder)
{
    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri) =>
        base.BuildChallengeUrl(properties, MemberProviders.Callback(origin, Options.CallbackPath, redirectUri));

    protected override Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context) =>
        base.ExchangeCodeAsync(MemberProviders.Callback(origin, Options.CallbackPath, context));
}

internal sealed class MemberAppleHandler(IOptionsMonitor<AppleAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, PublicOrigin origin)
    : AppleAuthenticationHandler(options, logger, encoder)
{
    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri) =>
        base.BuildChallengeUrl(properties, MemberProviders.Callback(origin, Options.CallbackPath, redirectUri));

    protected override Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context) =>
        base.ExchangeCodeAsync(MemberProviders.Callback(origin, Options.CallbackPath, context));
}

// The GitHub handler takes the primary address of the account whether or not GitHub verified it. This one takes it
// only when it is verified, so a sign-in carries a verified address or none.
internal sealed class MemberGitHubHandler(IOptionsMonitor<GitHubAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, PublicOrigin origin)
    : GitHubAuthenticationHandler(options, logger, encoder)
{
    public const string EmailScope = "user:email";

    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri) =>
        base.BuildChallengeUrl(properties, MemberProviders.Callback(origin, Options.CallbackPath, redirectUri));

    protected override Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context) =>
        base.ExchangeCodeAsync(MemberProviders.Callback(origin, Options.CallbackPath, context));

    protected override async Task<string?> GetEmailAsync(OAuthTokenResponse tokens)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Options.UserEmailsEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var response = await Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Context.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            MemberLog.ProviderAddressUnavailable(Logger, Scheme.Name[MemberProvider.SchemePrefix.Length..], (int)response.StatusCode);
            return null;
        }

        using var addresses = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));
        if (addresses.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var address in addresses.RootElement.EnumerateArray())
        {
            if (IsTrue(address, "primary") && IsTrue(address, "verified")
                && address.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String)
            {
                return email.GetString();
            }
        }

        return null;
    }

    private static bool IsTrue(JsonElement address, string name) =>
        address.ValueKind == JsonValueKind.Object && address.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

using System.Text.Encodings.Web;
using FluentCMS.Core.Identity.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Identity;

internal static class FluentCmsAuthentication
{
    // The scheme the FluentCMS endpoints authenticate with.
    public const string Scheme = "FluentCms";
}

// Picks the scheme for a request: the API key's when it carries one, and otherwise the host's default authenticate scheme.
// With no default, or a default that is this scheme, the request has no result here, so it is anonymous. A policy scheme
// cannot say that: its handler throws when it has nowhere to forward.
internal sealed class FluentCmsAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAuthenticationSchemeProvider schemes)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync() =>
        await TargetAsync() is { } target ? await Context.AuthenticateAsync(target) : AuthenticateResult.NoResult();

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (await TargetAsync() is { } target)
        {
            await Context.ChallengeAsync(target, properties);
            return;
        }

        await base.HandleChallengeAsync(properties);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (await TargetAsync() is { } target)
        {
            await Context.ForbidAsync(target, properties);
            return;
        }

        await base.HandleForbiddenAsync(properties);
    }

    private async Task<string?> TargetAsync()
    {
        if (ApiKeyAuthentication.IsPresented(Request))
        {
            return ApiKeyAuthentication.Scheme;
        }

        var fallback = (await schemes.GetDefaultAuthenticateSchemeAsync())?.Name;
        return fallback is null || fallback == Scheme.Name ? null : fallback;
    }
}

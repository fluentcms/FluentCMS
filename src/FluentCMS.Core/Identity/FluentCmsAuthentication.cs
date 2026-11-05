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

internal sealed class FluentCmsAuthenticationOptions : AuthenticationSchemeOptions
{
    // The bearer token scheme a request without an API key goes to. The authorization server (#2778) sets it; until then
    // such a request is anonymous here. It is never the host's default, which may be a cookie scheme and would open the
    // APIs to cross-site requests.
    public string? BearerScheme { get; set; }
}

// Picks the scheme for a request: the API key's when it carries one, and otherwise the bearer scheme when one is set.
// With neither the request has no result here, so it is anonymous. A policy scheme cannot say that: its handler throws
// when it has nowhere to forward.
internal sealed class FluentCmsAuthenticationHandler(IOptionsMonitor<FluentCmsAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<FluentCmsAuthenticationOptions>(options, logger, encoder)
{
    // A host scheme that forwards back here would otherwise recurse until the stack overflows.
    private static readonly object Entered = new();

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Target() is not { } target || Context.Items.ContainsKey(Entered))
        {
            return AuthenticateResult.NoResult();
        }

        Context.Items[Entered] = true;
        try
        {
            return await Context.AuthenticateAsync(target);
        }
        finally
        {
            Context.Items.Remove(Entered);
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Target() is { } target && !Context.Items.ContainsKey(Entered))
        {
            Context.Items[Entered] = true;
            try
            {
                await Context.ChallengeAsync(target, properties);
            }
            finally
            {
                Context.Items.Remove(Entered);
            }

            return;
        }

        await base.HandleChallengeAsync(properties);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (Target() is { } target && !Context.Items.ContainsKey(Entered))
        {
            Context.Items[Entered] = true;
            try
            {
                await Context.ForbidAsync(target, properties);
            }
            finally
            {
                Context.Items.Remove(Entered);
            }

            return;
        }

        await base.HandleForbiddenAsync(properties);
    }

    private string? Target()
    {
        if (ApiKeyAuthentication.IsPresented(Request))
        {
            return ApiKeyAuthentication.Scheme;
        }

        var bearer = Options.BearerScheme;
        return string.IsNullOrEmpty(bearer) || bearer == Scheme.Name || bearer == ApiKeyAuthentication.Scheme ? null : bearer;
    }
}

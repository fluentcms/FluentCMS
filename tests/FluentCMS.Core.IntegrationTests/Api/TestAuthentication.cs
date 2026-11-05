using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.IntegrationTests.Api;

// Signs a request in from headers, with the claims the real handlers will set: X-Test-Sub, X-Test-Kind and X-Test-Limit.
internal sealed class AuthCalls
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Add() => Interlocked.Increment(ref _count);
}

// Counts its runs, so that a test can tell whether a request got as far as authentication.
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AuthCalls calls)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        calls.Add();
        if (!Request.Headers.TryGetValue("X-Test-Sub", out var subject))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims = [new(CmsClaimTypes.Subject, subject.ToString())];
        if (Request.Headers.TryGetValue("X-Test-Kind", out var kind))
        {
            claims.Add(new Claim(CmsClaimTypes.Kind, kind.ToString()));
        }

        if (Request.Headers.TryGetValue("X-Test-Limit", out var limit))
        {
            claims.Add(new Claim(CmsClaimTypes.RateLimit, limit.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

// The default scheme of a host with cookies: it signs everyone in as the same browser user.
internal sealed class BrowserAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Browser";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity([new Claim(CmsClaimTypes.Subject, "browser")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

internal static class TestAuthentication
{
    public static IServiceCollection AddTestAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<AuthCalls>();
        services.AddAuthentication(TestAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        return services;
    }
}

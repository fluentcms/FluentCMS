using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.IntegrationTests.Delivery;

// Stands in for a bearer scheme behind the FluentCms scheme: it writes the claims the handlers write, from test headers.
// A request without the kind header is anonymous.
internal sealed class TestScheme(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "Test";
    public const string KindHeader = "X-Test-Kind";
    public const string RolesHeader = "X-Test-Roles";
    public const string ScopeHeader = "X-Test-Scope";
    public const string SitesHeader = "X-Test-Sites";
    public const string FailHeader = "X-Test-Fail";

    public static readonly Guid Subject = new("0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f");

    public static void Add(IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestScheme>(Name, null);
        services.Configure<FluentCmsAuthenticationOptions>(FluentCmsAuthentication.Scheme, options => options.BearerScheme = Name);
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers;
        if (headers.ContainsKey(FailHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("The test credentials are not valid."));
        }

        if (headers[KindHeader].FirstOrDefault() is not { } kind)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims =
        [
            new(CmsClaimTypes.Subject, Subject.ToString()),
            new(CmsClaimTypes.Name, "Test key"),
            new(CmsClaimTypes.Kind, kind),
        ];
        Add(claims, CmsClaimTypes.Roles, headers[RolesHeader].FirstOrDefault());
        Add(claims, CmsClaimTypes.Scope, headers[ScopeHeader].FirstOrDefault());
        Add(claims, CmsClaimTypes.Sites, headers[SitesHeader].FirstOrDefault());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
    }

    private static void Add(List<Claim> claims, string type, string? value)
    {
        if (value is not null)
        {
            claims.Add(new(type, value));
        }
    }
}

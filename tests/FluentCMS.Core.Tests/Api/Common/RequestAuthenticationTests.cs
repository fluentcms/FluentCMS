using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public sealed class RequestAuthenticationTests
{
    [Fact]
    public async Task WithoutTheFluentCmsSchemeTheRequestUserIsUsed()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "x")], "Default"));
        var httpContext = Context(services => services.AddAuthentication());
        httpContext.User = user;

        (await RequestAuthentication.UserAsync(httpContext)).ShouldBeSameAs(user);
    }

    [Fact]
    public async Task TheFluentCmsSchemeWinsOverTheRequestUser()
    {
        var httpContext = Context(services => services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, KeyScheme>(FluentCmsAuthentication.Scheme, null));
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "cookie")], "Default"));
        httpContext.Request.Headers["X-Key"] = "key";

        var user = await RequestAuthentication.UserAsync(httpContext);

        user.FindFirst("sub")!.Value.ShouldBe("key");
        httpContext.User.ShouldBeSameAs(user);
    }

    [Fact]
    public async Task NoResultFromTheFluentCmsSchemeIsAnonymous()
    {
        var httpContext = Context(services => services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, KeyScheme>(FluentCmsAuthentication.Scheme, null));

        (await RequestAuthentication.UserAsync(httpContext)).Identity!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task AFailedFluentCmsSchemeIsRefused()
    {
        var httpContext = Context(services => services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, KeyScheme>(FluentCmsAuthentication.Scheme, null));
        httpContext.Request.Headers["X-Key"] = "bad";

        await Should.ThrowAsync<RequestAuthenticationException>(() => RequestAuthentication.UserAsync(httpContext));
    }

    private static DefaultHttpContext Context(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection().AddLogging();
        configure(services);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private sealed class KeyScheme(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(Request.Headers["X-Key"].FirstOrDefault() switch
            {
                null => AuthenticateResult.NoResult(),
                "bad" => AuthenticateResult.Fail("bad key"),
                var key => AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", key)], Scheme.Name)), Scheme.Name)),
            });
    }
}

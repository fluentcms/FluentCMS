using System.Security.Claims;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Common;

public sealed class RequestPrincipalTests
{
    private static readonly Guid Id = new("0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f");

    [Fact]
    public void AnUnauthenticatedRequestIsAnonymous()
    {
        RequestPrincipal.From(new ClaimsPrincipal(new ClaimsIdentity())).ShouldBeSameAs(Principal.Anonymous);
    }

    [Fact]
    public void TheClaimsBecomeThePrincipal()
    {
        var principal = RequestPrincipal.From(User(
            new(CmsClaimTypes.Kind, nameof(PrincipalKind.ApiKey)),
            new(CmsClaimTypes.Name, "Website"),
            new(CmsClaimTypes.Scope, "delivery"),
            new(CmsClaimTypes.Scope, "preview"),
            new(CmsClaimTypes.Sites, "a b"),
            new("region", "eu")));

        principal.Kind.ShouldBe(PrincipalKind.ApiKey);
        principal.Id.ShouldBe(Id);
        principal.Name.ShouldBe("Website");
        principal.Claim(CmsClaimTypes.Scope).ShouldBe("delivery preview");
        principal.Claim("region").ShouldBe("eu");
        principal.Claims.Keys.ShouldNotContain(CmsClaimTypes.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("System")]
    [InlineData("Anonymous")]
    [InlineData("3")]
    [InlineData("AdminUser, Member")]
    [InlineData("apikey")]
    public void AKindThatIsNotAnAuthenticatedPrincipalIsRefused(string? kind)
    {
        Claim[] claims = kind is null ? [] : [new(CmsClaimTypes.Kind, kind)];

        Should.Throw<RequestAuthenticationException>(() => RequestPrincipal.From(User(claims)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", new string[0])]
    [InlineData("0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f nonsense", new[] { "0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f" })]
    public void TheBindingIsReadFromTheSitesClaim(string? claim, string[]? expected)
    {
        KeyValuePair<string, string>[] claims = claim is null ? [] : [KeyValuePair.Create(CmsClaimTypes.Sites, claim)];
        var principal = new Principal(PrincipalKind.ApiKey, Id, "Key", claims);

        var sites = RequestPrincipal.BoundSites(principal);

        (sites?.Select(site => site.ToString()).ToArray()).ShouldBe(expected);
    }

    private static ClaimsPrincipal User(params Claim[] claims) =>
        new(new ClaimsIdentity([new Claim(CmsClaimTypes.Subject, Id.ToString()), .. claims], "Test"));
}

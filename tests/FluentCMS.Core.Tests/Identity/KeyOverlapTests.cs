using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Identity.PreviewTokens;
using OpenIddict.Server;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class KeyOverlapTests
{
    private static readonly TimeSpan Allowance = TimeSpan.FromSeconds(30) + TimeSpan.FromMinutes(5);

    [Fact]
    public void ByDefaultAKeyIsAcceptedForTheLifetimeOfARefreshTokenAndUnderSixMinutesMore()
    {
        var clients = new ClientOptions();
        var server = new OpenIddictServerOptions { AccessTokenLifetime = clients.AccessTokenLifetime, RefreshTokenLifetime = clients.RefreshTokenLifetime };

        KeyOverlap.Of(server, new PreviewTokenOptions(), new KeyRotationOptions()).ShouldBe(TimeSpan.FromDays(14) + Allowance);
        KeyOverlap.AtStart(clients, new PreviewTokenOptions()).ShouldBe(TimeSpan.FromDays(14) + Allowance);
    }

    [Fact]
    public void TheLongestLifetimeCountsWhicheverTokenHasIt()
    {
        var server = new OpenIddictServerOptions { AccessTokenLifetime = TimeSpan.FromMinutes(5), RefreshTokenLifetime = TimeSpan.FromMinutes(30) };

        KeyOverlap.Of(server, new PreviewTokenOptions { MaximumLifetime = TimeSpan.FromDays(1) }, new KeyRotationOptions()).ShouldBe(TimeSpan.FromDays(1) + Allowance);

        // A lifetime a host set on OpenIddict itself.
        server.AuthorizationCodeLifetime = TimeSpan.FromDays(2);
        KeyOverlap.Of(server, new PreviewTokenOptions { MaximumLifetime = TimeSpan.FromDays(1) }, new KeyRotationOptions()).ShouldBe(TimeSpan.FromDays(2) + Allowance);
    }

    [Fact]
    public void AConfiguredOverlapCountsOnlyWhenItIsTheLongerOne()
    {
        var server = new OpenIddictServerOptions { RefreshTokenLifetime = TimeSpan.FromDays(14) };

        KeyOverlap.Of(server, new PreviewTokenOptions(), new KeyRotationOptions { Overlap = TimeSpan.FromDays(30) }).ShouldBe(TimeSpan.FromDays(30));
        KeyOverlap.Of(server, new PreviewTokenOptions(), new KeyRotationOptions { Overlap = TimeSpan.FromDays(1) }).ShouldBe(TimeSpan.FromDays(14) + Allowance);
    }
}

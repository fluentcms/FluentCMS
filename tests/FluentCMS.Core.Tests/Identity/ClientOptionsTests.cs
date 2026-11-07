using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class ClientOptionsTests
{
    [Fact]
    public void TheLifetimesKeepTheLibraryDefaultsAndSetNoSessionLimit()
    {
        var options = new ClientOptions();

        options.AccessTokenLifetime.ShouldBe(TimeSpan.FromHours(1));
        options.RefreshTokenLifetime.ShouldBe(TimeSpan.FromDays(14));
        options.SignInCookieLifetime.ShouldBe(TimeSpan.FromDays(14));
        options.AbsoluteSessionLifetime.ShouldBeNull();
        Should.NotThrow(options.Validate);
    }

    [Fact]
    public void ALifetimeOutOfRangeIsRefusedWithItsName()
    {
        (Action<ClientOptions> Set, string Name)[] cases =
        [
            (options => options.AccessTokenLifetime = TimeSpan.Zero, nameof(ClientOptions.AccessTokenLifetime)),
            (options => options.RefreshTokenLifetime = TimeSpan.FromDays(-1), nameof(ClientOptions.RefreshTokenLifetime)),
            (options => options.SignInCookieLifetime = TimeSpan.FromDays(366), nameof(ClientOptions.SignInCookieLifetime)),
            (options => options.AbsoluteSessionLifetime = TimeSpan.Zero, nameof(ClientOptions.AbsoluteSessionLifetime)),
            (options => options.AccessTokenLifetime = TimeSpan.FromDays(15), nameof(ClientOptions.RefreshTokenLifetime)),
        ];

        foreach (var (set, name) in cases)
        {
            var options = new ClientOptions();
            set(options);
            var exception = Should.Throw<FluentCmsConfigurationException>(options.Validate);
            exception.Code.ShouldBe(OidcErrorCodes.InvalidClients);
            exception.Detail.ShouldContain(name);
        }
    }

    [Fact]
    public void ASessionLimitWithinRangeIsAccepted()
    {
        var options = new ClientOptions { AbsoluteSessionLifetime = TimeSpan.FromHours(12), AccessTokenLifetime = TimeSpan.FromMinutes(5) };

        Should.NotThrow(options.Validate);
    }
}

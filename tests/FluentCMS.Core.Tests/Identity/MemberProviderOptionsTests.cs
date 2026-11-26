using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Events;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class MemberProviderOptionsTests
{
    [Fact]
    public void NoProviderIsConfiguredUnlessAHostAddsOne()
    {
        var options = new MemberOptions();

        options.Providers.ShouldBeEmpty();
        new MemberProvider().LinkVerifiedEmail.ShouldBeFalse();
        new MemberProvider().RequireHttpsMetadata.ShouldBeTrue();
    }

    [Fact]
    public void AValidProviderOfEveryTypePasses()
    {
        var options = new MemberOptions();
        options.Providers["google"] = new MemberProvider { Type = MemberProviderTypes.Google, ClientId = "id", ClientSecret = "secret", LinkVerifiedEmail = true };
        options.Providers["microsoft"] = new MemberProvider { Type = "microsoft", ClientId = "id", ClientSecret = "secret" };
        options.Providers["git-hub-1"] = new MemberProvider { Type = "GITHUB", ClientId = "id", ClientSecret = "secret", Scopes = { "read:org" } };
        options.Providers["corp"] = new MemberProvider { Type = MemberProviderTypes.Oidc, ClientId = "id", Authority = "https://id.example.com/realms/members" };
        options.Providers["local"] = new MemberProvider { Type = MemberProviderTypes.Oidc, ClientId = "id", ClientSecret = "secret", Authority = "http://localhost:8080", RequireHttpsMetadata = false };

        Should.NotThrow(options.Validate);
        options.Providers["GOOGLE"].ShouldBeSameAs(options.Providers["google"]);
        options.Providers["microsoft"].Kind.ShouldBe(MemberProviderKind.Microsoft);
        options.Providers["git-hub-1"].Kind.ShouldBe(MemberProviderKind.GitHub);
    }

    [Fact]
    public void TheButtonIsNamedByTheTypeOrByTheProviderForGenericOidc()
    {
        new MemberProvider { Type = MemberProviderTypes.Google }.Name("g").ShouldBe("Google");
        new MemberProvider { Type = MemberProviderTypes.Microsoft }.Name("m").ShouldBe("Microsoft");
        new MemberProvider { Type = MemberProviderTypes.GitHub }.Name("gh").ShouldBe("GitHub");
        new MemberProvider { Type = MemberProviderTypes.Oidc }.Name("corp").ShouldBe("corp");
        new MemberProvider { Type = MemberProviderTypes.Google, DisplayName = "Work account" }.Name("g").ShouldBe("Work account");
        MemberProvider.SchemeOf("google").ShouldBe("member-google");
    }

    // The name is kept with every login as it is written, so it has one way to be written: lower-case.
    [Theory]
    [InlineData("corp/x")]
    [InlineData("corp.x")]
    [InlineData("corp x")]
    [InlineData("corp_x")]
    [InlineData("Google")]
    [InlineData("CORP")]
    [InlineData("")]
    public void AProviderNameIsLowerCaseAndFitsAPath(string name)
    {
        var refused = Refused(name, new MemberProvider { Type = MemberProviderTypes.Google, ClientId = "id", ClientSecret = "secret" });

        refused.ShouldStartWith($"FCMS3180: Providers:{name}:");
    }

    // A sign-in through a provider names the provider as its method in the sign-in events.
    [Fact]
    public void AProviderIsNotNamedLikeASignInMethodOfThePool()
    {
        var refused = Refused(MemberLoginMethods.Password, new MemberProvider { Type = MemberProviderTypes.Google, ClientId = "id", ClientSecret = "secret" });

        refused.ShouldStartWith("FCMS3180: Providers:password: 'password' is a sign-in method");

        // The other method has a character a name cannot have, and is refused for that already.
        Refused(MemberLoginMethods.DirectLogin, new MemberProvider { Type = MemberProviderTypes.Google, ClientId = "id", ClientSecret = "secret" })
            .ShouldStartWith("FCMS3180: Providers:direct_login:");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Facebook")]
    [InlineData("0")]
    [InlineData("Google,GitHub")]
    [InlineData("EntraId")]
    [InlineData("Apple")]
    [InlineData("apple")]
    public void ATypeThatIsNoneOfTheFourIsRefused(string? type)
    {
        var refused = Refused("p", new MemberProvider { Type = type, ClientId = "id", ClientSecret = "secret" });

        refused.ShouldStartWith("FCMS3180: Providers:p:Type");
        refused.ShouldContain("Google, Microsoft, GitHub, Oidc");
    }

    [Theory]
    [InlineData(MemberProviderTypes.Google)]
    [InlineData(MemberProviderTypes.Microsoft)]
    [InlineData(MemberProviderTypes.GitHub)]
    [InlineData(MemberProviderTypes.Oidc)]
    public void EveryTypeNeedsAClientId(string type)
    {
        var provider = Valid(type);
        provider.ClientId = " ";

        Refused("p", provider).ShouldStartWith("FCMS3180: Providers:p:ClientId");
    }

    [Theory]
    [InlineData(MemberProviderTypes.Google)]
    [InlineData(MemberProviderTypes.Microsoft)]
    [InlineData(MemberProviderTypes.GitHub)]
    public void TheOAuthProvidersNeedAClientSecret(string type)
    {
        var provider = Valid(type);
        provider.ClientSecret = null;

        Refused("p", provider).ShouldStartWith("FCMS3180: Providers:p:ClientSecret");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("id.example.com")]
    [InlineData("http://id.example.com")]
    [InlineData("https://id.example.com?tenant=1")]
    [InlineData("ftp://id.example.com")]
    public void GenericOidcNeedsAnHttpsAuthority(string? authority)
    {
        var provider = Valid(MemberProviderTypes.Oidc);
        provider.Authority = authority;

        Refused("p", provider).ShouldStartWith("FCMS3180: Providers:p:Authority");
    }

    [Fact]
    public void LinkVerifiedEmailIsNotOfferedForMicrosoft()
    {
        var provider = Valid(MemberProviderTypes.Microsoft);
        provider.LinkVerifiedEmail = true;

        Refused("p", provider).ShouldStartWith("FCMS3180: Providers:p:LinkVerifiedEmail");

        foreach (var type in new[] { MemberProviderTypes.Google, MemberProviderTypes.GitHub, MemberProviderTypes.Oidc })
        {
            var linking = Valid(type);
            linking.LinkVerifiedEmail = true;
            Should.NotThrow(() => linking.Validate("p"));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("openid email")]
    public void AScopeIsOneWord(string scope)
    {
        var provider = Valid(MemberProviderTypes.Google);
        provider.Scopes.Add(scope);

        Refused("p", provider).ShouldStartWith("FCMS3180: Providers:p:Scopes");
    }

    private static MemberProvider Valid(string type) => type switch
    {
        MemberProviderTypes.Oidc => new MemberProvider { Type = type, ClientId = "id", Authority = "https://id.example.com" },
        _ => new MemberProvider { Type = type, ClientId = "id", ClientSecret = "secret" },
    };

    private static string Refused(string name, MemberProvider provider)
    {
        var options = new MemberOptions();
        options.Providers[name] = provider;

        var exception = Should.Throw<FluentCmsConfigurationException>(options.Validate);
        exception.Code.ShouldBe("FCMS3180");
        return exception.Message;
    }
}

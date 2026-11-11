using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.Identity.Oidc;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class MemberOptionsTests
{
    [Fact]
    public void RegistrationAndTheDirectLoginAreOffAndTheLifetimesAreThoseOfTheSpecification()
    {
        var options = new MemberOptions();

        options.AllowRegistration.ShouldBeFalse();
        options.AllowDirectLogin.ShouldBeFalse();
        options.EmailVerificationLifetime.ShouldBe(TimeSpan.FromHours(24));
        options.PasswordResetLifetime.ShouldBe(TimeSpan.FromHours(1));
        options.UnverifiedMemberLifetime.ShouldBe(TimeSpan.FromDays(7));
        options.Links.VerifyEmail.ShouldBeNull();
        options.Links.ResetPassword.ShouldBeNull();
        Should.NotThrow(options.Validate);
    }

    [Fact]
    public void TheMemberPoolHasItsOwnIdentityOptionsWithSignInForAVerifiedAddressOnly()
    {
        var members = new MemberOptions();
        members.Password.RequiredLength = 20;
        members.Lockout.MaxFailedAccessAttempts = 3;

        var identity = new MemberIdentityOptions(members).Value;

        identity.Password.RequiredLength.ShouldBe(20);
        identity.Lockout.MaxFailedAccessAttempts.ShouldBe(3);
        identity.Lockout.AllowedForNewUsers.ShouldBeTrue();
        identity.SignIn.RequireConfirmedEmail.ShouldBeTrue();
        identity.User.RequireUniqueEmail.ShouldBeTrue();
        identity.Tokens.EmailConfirmationTokenProvider.ShouldBe(MemberEmailTokenProvider.ProviderName);
        identity.Tokens.PasswordResetTokenProvider.ShouldBe(MemberPasswordResetTokenProvider.ProviderName);
        identity.Tokens.ProviderMap.Keys.ShouldBe([MemberEmailTokenProvider.ProviderName, MemberPasswordResetTokenProvider.ProviderName], ignoreOrder: true);

        // The names are the purposes of the tokens, so neither is the admin pool's.
        MemberPasswordResetTokenProvider.ProviderName.ShouldNotBe(PasswordResetTokenProvider.ProviderName);
        new IdentityOptions().SignIn.RequireConfirmedEmail.ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://app.example.com/verify?email={email}&token={token}")]
    [InlineData("https://app.example.com/verify/{token}?address={email}")]
    [InlineData("https://app.example.com/#/verify/{email}/{token}")]
    [InlineData("http://localhost:3000/verify?e={email}&t={token}")]
    public void ALinkTemplateOfTheFrontEndIsAccepted(string template)
    {
        var options = new MemberOptions();
        options.Links.VerifyEmail = template;
        options.Links.ResetPassword = template;

        Should.NotThrow(options.Validate);
    }

    [Theory]
    [InlineData("/verify?email={email}&token={token}")]
    [InlineData("app.example.com/verify?email={email}&token={token}")]
    [InlineData("ftp://app.example.com/verify?email={email}&token={token}")]
    [InlineData("javascript:alert('{email}{token}')")]
    [InlineData("https://app.example.com/verify?email={email}")]
    [InlineData("https://app.example.com/verify?token={token}")]
    [InlineData("https://{email}.example.com/verify?token={token}")]
    [InlineData("https://app.example.com.{token}/verify?email={email}")]
    [InlineData("https://{token}@app.example.com/verify?email={email}")]
    [InlineData("https://user@app.example.com/verify?email={email}&token={token}")]
    public void ALinkTemplateThatIsNoUrlOrLetsAValueNameTheHostIsRefused(string template)
    {
        var verify = new MemberOptions();
        verify.Links.VerifyEmail = template;
        var refused = Should.Throw<FluentCmsConfigurationException>(verify.Validate);
        refused.Code.ShouldBe("FCMS3160");
        refused.Detail.ShouldStartWith("Links:VerifyEmail");

        var reset = new MemberOptions();
        reset.Links.ResetPassword = template;
        Should.Throw<FluentCmsConfigurationException>(reset.Validate).Detail.ShouldStartWith("Links:ResetPassword");
    }

    [Fact]
    public void AMemberIsNotRemovedWhileItsVerificationLinkStillWorks()
    {
        var options = new MemberOptions { EmailVerificationLifetime = TimeSpan.FromDays(10) };

        Should.Throw<FluentCmsConfigurationException>(options.Validate).Detail.ShouldStartWith(nameof(MemberOptions.UnverifiedMemberLifetime));
    }

    [Theory]
    [InlineData("mia@example.com", true)]
    [InlineData("mia+news@sub.example.com", true)]
    [InlineData("o'brien@example.com", true)]
    [InlineData("", false)]
    [InlineData("mia", false)]
    [InlineData("@example.com", false)]
    [InlineData("mia@", false)]
    [InlineData("mia@@example.com", false)]
    [InlineData("mia @example.com", false)]
    [InlineData("mia@example.com\r\nBcc: other@example.com", false)]
    [InlineData("Mia <mia@example.com>", false)]
    [InlineData("mia@example.com,other@example.com", false)]
    public void AnAddressIsOneMailboxAndNothingAMailHeaderCouldBeBrokenWith(string address, bool valid) =>
        MemberValidator.IsEmail(address).ShouldBe(valid);

    [Fact]
    public void AnAddressGetsOneMailAMinuteAndAnotherAddressIsNotHeldBack()
    {
        var time = new Clock(new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero));
        var limiter = new MemberMailLimiter(time);

        limiter.TryAcquire("MIA@EXAMPLE.COM").ShouldBeTrue();
        limiter.TryAcquire("MIA@EXAMPLE.COM").ShouldBeFalse();
        limiter.TryAcquire("SAM@EXAMPLE.COM").ShouldBeTrue();

        time.Now += MemberMailLimiter.Window - TimeSpan.FromSeconds(1);
        limiter.TryAcquire("MIA@EXAMPLE.COM").ShouldBeFalse();

        time.Now += TimeSpan.FromSeconds(1);
        limiter.TryAcquire("MIA@EXAMPLE.COM").ShouldBeTrue();
        limiter.TryAcquire("MIA@EXAMPLE.COM").ShouldBeFalse();
    }

    [Fact]
    public void AClientOfTheMemberPoolAsksForAMembersScopesOnlyAndIsNeverAServiceAccount()
    {
        Should.NotThrow(() => Clients(new ClientRegistration { Pool = ClientPool.Members, RedirectUris = { "https://app.example.com/cb" }, Scopes = { "openid", "profile", "email", "offline_access" } }).Validate());
        Should.NotThrow(() => Clients(new ClientRegistration { Pool = ClientPool.Members, AllowDirectLogin = true }).Validate());

        (ClientRegistration Client, string Names)[] refused =
        [
            (new ClientRegistration { Pool = ClientPool.Members, RedirectUris = { "https://app.example.com/cb" }, Scopes = { "openid", "management" } }, nameof(ClientRegistration.Scopes)),
            (new ClientRegistration { Pool = ClientPool.Members, RedirectUris = { "https://app.example.com/cb" }, Scopes = { "draft" } }, nameof(ClientRegistration.Scopes)),
            (new ClientRegistration { Pool = ClientPool.Members, RedirectUris = { "https://app.example.com/cb" }, Scopes = { "mcp" } }, nameof(ClientRegistration.Scopes)),
            (new ClientRegistration { Pool = ClientPool.Members, RedirectUris = { "https://app.example.com/cb" }, Roles = { "Editor" } }, nameof(ClientRegistration.Roles)),
            (new ClientRegistration { Pool = ClientPool.Members, RedirectUris = { "https://app.example.com/cb" }, RequireConsent = true }, nameof(ClientRegistration.RequireConsent)),
            (new ClientRegistration { Pool = ClientPool.Members }, nameof(ClientRegistration.RedirectUris)),
            (new ClientRegistration { RedirectUris = { "https://admin.example.com/cb" }, AllowDirectLogin = true }, nameof(ClientRegistration.AllowDirectLogin)),
        ];

        foreach (var (client, names) in refused)
        {
            var exception = Should.Throw<FluentCmsConfigurationException>(() => Clients(client).Validate());
            exception.Code.ShouldBe(OidcErrorCodes.InvalidClients);
            exception.Detail.ShouldContain(names);
        }
    }

    [Fact]
    public void AClientOfTheMemberPoolIsRegisteredWithoutClientCredentialsAndWithTheDirectLoginGrant()
    {
        var members = ClientRegistrationService.Describe("app", new ClientRegistration
        {
            Pool = ClientPool.Members,
            AllowDirectLogin = true,
            RedirectUris = { "https://app.example.com/cb" },
            Scopes = { "openid", "profile", "offline_access" },
        });

        members.Properties.ContainsKey(AuthorizationServer.MembersProperty).ShouldBeTrue();
        members.Properties.ContainsKey(AuthorizationServer.DirectLoginProperty).ShouldBeTrue();
        members.Permissions.ShouldContain(OpenIddictConstants.Permissions.Prefixes.GrantType + AuthorizationServer.MemberPasswordGrant);
        members.Permissions.ShouldContain(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
        members.Permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        members.Permissions.ShouldNotContain(OpenIddictConstants.Permissions.Endpoints.Introspection);
        members.Permissions.ShouldNotContain(OpenIddictConstants.Permissions.Prefixes.Scope + "management");

        // A confidential one is no service account either.
        var confidential = ClientRegistrationService.Describe("backend", new ClientRegistration
        {
            Pool = ClientPool.Members,
            Secret = Guid.NewGuid().ToString("N"),
            RedirectUris = { "https://app.example.com/cb" },
        });
        confidential.Permissions.ShouldNotContain(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        confidential.Properties.ContainsKey(AuthorizationServer.DirectLoginProperty).ShouldBeFalse();

        // A client of the admin pool is as it was: no mark, and no direct login grant.
        var admin = ClientRegistrationService.Describe("admin", new ClientRegistration { RedirectUris = { "https://admin.example.com/cb" }, Scopes = { "openid", "management" } });
        admin.Properties.ContainsKey(AuthorizationServer.MembersProperty).ShouldBeFalse();
        admin.Permissions.ShouldNotContain(OpenIddictConstants.Permissions.Prefixes.GrantType + AuthorizationServer.MemberPasswordGrant);
    }

    [Fact]
    public void TheAdminApplicationsClientIsNotOfTheMemberPool()
    {
        var options = new ClientOptions { AdminClientId = "app" };
        options.Applications["app"] = new ClientRegistration { Pool = ClientPool.Members, RedirectUris = { "https://app.example.com/cb" } };

        var exception = Should.Throw<FluentCmsConfigurationException>(options.Validate);
        exception.Code.ShouldBe(OidcErrorCodes.InvalidClients);
        exception.Detail.ShouldStartWith(nameof(ClientOptions.AdminClientId));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ClientOptions Clients(ClientRegistration client)
    {
        var options = new ClientOptions();
        options.Applications["app"] = client;
        return options;
    }
}

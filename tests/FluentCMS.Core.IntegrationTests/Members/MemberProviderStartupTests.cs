using System.Security.Cryptography;
using AspNet.Security.OAuth.Apple;
using AspNet.Security.OAuth.GitHub;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Identity.Members;
using FluentCMS.Core.IntegrationTests.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Members;

// The member providers as a host's configuration names them: what starts, and what stops the host.
public sealed class MemberProviderStartupTests : IAsyncDisposable
{
    private const string Providers = "FluentCms:Members:Providers:";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Theory]
    [InlineData("a missing client id", "FluentCms:Members:Providers:google:ClientId")]
    [InlineData("an unknown type", "FluentCms:Members:Providers:social:Type")]
    [InlineData("no type", "FluentCms:Members:Providers:social:Type")]
    [InlineData("the name of another provider", "FluentCms:Members:Providers:shared: 'member-shared'")]
    [InlineData("a missing client secret", "FluentCms:Members:Providers:github:ClientSecret")]
    [InlineData("no authority", "FluentCms:Members:Providers:corp:Authority")]
    [InlineData("no private key", "FluentCms:Members:Providers:apple:PrivateKey")]
    [InlineData("linking for Microsoft", "FluentCms:Members:Providers:microsoft:LinkVerifiedEmail")]
    [InlineData("a name that is no path segment", "FluentCms:Members:Providers:my.provider:")]
    public async Task AProviderThatIsNotValidStopsTheHostWithItsCode(string what, string key)
    {
        var settings = what switch
        {
            "a missing client id" => Settings(("google:Type", "Google"), ("google:ClientSecret", "secret")),
            "an unknown type" => Settings(("social:Type", "Facebook"), ("social:ClientId", "id"), ("social:ClientSecret", "secret")),
            "no type" => Settings(("social:ClientId", "id"), ("social:ClientSecret", "secret")),

            // An admin provider whose name is the scheme a member provider gets: the two would be one scheme.
            "the name of another provider" => new Dictionary<string, string?>
            {
                ["FluentCms:Sso:Providers:member-shared:Authority"] = "https://id.example.com",
                ["FluentCms:Sso:Providers:member-shared:ClientId"] = "cms",
                [Providers + "shared:Type"] = "Google",
                [Providers + "shared:ClientId"] = "id",
                [Providers + "shared:ClientSecret"] = "secret",
            },
            "a missing client secret" => Settings(("github:Type", "GitHub"), ("github:ClientId", "id")),
            "no authority" => Settings(("corp:Type", "Oidc"), ("corp:ClientId", "id")),
            "no private key" => Settings(("apple:Type", "Apple"), ("apple:ClientId", "id"), ("apple:TeamId", "TEAM123456"), ("apple:KeyId", "KEY1234567")),
            "linking for Microsoft" => Settings(("microsoft:Type", "Microsoft"), ("microsoft:ClientId", "id"), ("microsoft:ClientSecret", "secret"), ("microsoft:LinkVerifiedEmail", "true")),
            _ => Settings(("my.provider:Type", "Google"), ("my.provider:ClientId", "id"), ("my.provider:ClientSecret", "secret")),
        };
        var app = _host.Build(Environments.Production, settings);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe("FCMS3180");
        exception.Message.ShouldStartWith($"FCMS3180: {key}");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    [Fact]
    public async Task EachTypeOfProviderIsASchemeOfItsOwnHandlerThatSignsIntoTheExternalCookieOfTheMemberPool()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var settings = Settings(
            ("google:Type", "google"), ("google:ClientId", "google-id"), ("google:ClientSecret", "google-secret"), ("google:Scopes:0", "https://www.googleapis.com/auth/user.birthday.read"),
            ("ms:Type", "Microsoft"), ("ms:ClientId", "ms-id"), ("ms:ClientSecret", "ms-secret"),
            ("github:Type", "GitHub"), ("github:ClientId", "github-id"), ("github:ClientSecret", "github-secret"), ("github:LinkVerifiedEmail", "true"),
            ("apple:Type", "Apple"), ("apple:ClientId", "com.example.web"), ("apple:TeamId", "TEAM123456"), ("apple:KeyId", "KEY1234567"), ("apple:PrivateKey", key.ExportPkcs8PrivateKeyPem()),
            ("corp:Type", "Oidc"), ("corp:ClientId", "corp-id"), ("corp:Authority", "https://id.example.com/"), ("corp:DisplayName", "Corp"), ("corp:Scopes:0", "groups"));

        // The same names in the admin pool are other schemes.
        settings["FluentCms:Sso:Providers:corp:Authority"] = "https://staff.example.com";
        settings["FluentCms:Sso:Providers:corp:ClientId"] = "staff";
        var app = _host.Build(Environments.Development, settings);
        await app.StartAsync(Token);

        var members = app.Services.GetRequiredService<MemberOptions>();
        members.Providers.Keys.ShouldBe(["google", "ms", "github", "apple", "corp"], ignoreOrder: true);
        members.Providers["github"].LinkVerifiedEmail.ShouldBeTrue();
        members.Providers["google"].LinkVerifiedEmail.ShouldBeFalse();

        var schemes = app.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var expected = new Dictionary<string, Type>
        {
            ["member-google"] = typeof(MemberGoogleHandler),
            ["member-ms"] = typeof(MemberMicrosoftHandler),
            ["member-github"] = typeof(MemberGitHubHandler),
            ["member-apple"] = typeof(MemberAppleHandler),
            ["member-corp"] = typeof(OpenIdConnectHandler),
        };
        foreach (var (name, handler) in expected)
        {
            var scheme = (await schemes.GetSchemeAsync(name)).ShouldNotBeNull(name);
            scheme.HandlerType.ShouldBe(handler);

            // No display name, so the admin login page, which lists the schemes that have one, does not offer it.
            scheme.DisplayName.ShouldBeNull();
        }

        (await schemes.GetSchemeAsync("corp")).ShouldNotBeNull().DisplayName.ShouldBe("corp");
        (await schemes.GetSchemeAsync(MemberProviders.ExternalScheme)).ShouldNotBeNull();

        var google = app.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get("member-google");
        Remote(google, "google");
        (google.ClientId, google.ClientSecret, google.UsePkce).ShouldBe(("google-id", "google-secret", true));
        google.Scope.ShouldBe(["openid", "profile", "email", "https://www.googleapis.com/auth/user.birthday.read"]);

        var microsoft = app.Services.GetRequiredService<IOptionsMonitor<MicrosoftAccountOptions>>().Get("member-ms");
        Remote(microsoft, "ms");
        (microsoft.ClientId, microsoft.ClientSecret, microsoft.UsePkce).ShouldBe(("ms-id", "ms-secret", true));

        var github = app.Services.GetRequiredService<IOptionsMonitor<GitHubAuthenticationOptions>>().Get("member-github");
        Remote(github, "github");
        github.Scope.ShouldBe(["user:email"]);
        github.ClaimActions.ShouldNotContain(action => action.ClaimType == System.Security.Claims.ClaimTypes.Email);

        var apple = app.Services.GetRequiredService<IOptionsMonitor<AppleAuthenticationOptions>>().Get("member-apple");
        Remote(apple, "apple");
        (apple.ClientId, apple.TeamId, apple.KeyId, apple.GenerateClientSecret, apple.ValidateTokens).ShouldBe(("com.example.web", "TEAM123456", "KEY1234567", true, true));
        apple.ClientSecret.ShouldBeEmpty();

        var oidc = app.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        var corp = oidc.Get("member-corp");
        Remote(corp, "corp");
        (corp.Authority, corp.ClientId, corp.ClientSecret, corp.ResponseType, corp.UsePkce).ShouldBe(("https://id.example.com/", "corp-id", null, "code", true));
        corp.Scope.ShouldBe(["openid", "profile", "email", "groups"]);
        corp.RemoteSignOutPath.Value.ShouldBe("/account/members/sso/corp/signout");

        // The admin provider of the same name keeps its own callback and cookie.
        var staff = oidc.Get("corp");
        staff.CallbackPath.Value.ShouldBe("/account/sso/corp");
        staff.SignInScheme.ShouldBe(Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme);
        staff.ClientId.ShouldBe("staff");

        // A scheme the options do not name is not configured as a member provider.
        app.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get("member-ms").ClientId.ShouldBeNullOrEmpty();

        static void Remote(RemoteAuthenticationOptions options, string name)
        {
            options.SignInScheme.ShouldBe("FluentCms.Members.External");
            options.CallbackPath.Value.ShouldBe("/account/members/sso/" + name);
            options.SaveTokens.ShouldBeFalse();
        }
    }

    private static Dictionary<string, string?> Settings(params (string Key, string Value)[] values) =>
        values.ToDictionary(pair => Providers + pair.Key, pair => (string?)pair.Value);
}

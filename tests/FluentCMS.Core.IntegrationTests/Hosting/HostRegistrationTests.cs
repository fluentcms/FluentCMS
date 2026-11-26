using System.Net;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

public sealed class HostRegistrationTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task AddFluentCmsAndMapFluentCmsGiveAWorkingHostOnSqlite()
    {
        var app = _host.Build(Environments.Production);

        await app.StartAsync(Token);

        app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.ShouldBeTrue();
        using var response = await app.GetTestClient().GetAsync(new Uri("/api/delivery/v1", UriKind.Relative), Token);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        File.Exists(_host.DatabaseFile).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Delivery", "/api/delivery/v1")]
    [InlineData("Management", "/api/management/v1")]
    [InlineData("Connect", "/connect")]
    [InlineData("Account", "/account")]
    [InlineData("Health", "/health")]
    public async Task EachRouteGroupIsUnderThePathBase(string group, string prefix)
    {
        var app = _host.Build(
            Environments.Development,
            new Dictionary<string, string?> { ["FluentCms:ReverseProxy:PathBase"] = "/cms" },
            configure: builder =>
            {
                foreach (var each in Enum.GetValues<FluentCmsEndpointGroup>())
                {
                    builder.Services.AddFluentCmsEndpoints(each, routes => routes.MapGet("/probe", () => each.ToString()));
                }
            });
        await app.StartAsync(Token);

        var client = app.GetTestClient();
        (await client.GetStringAsync(new Uri($"/cms{prefix}/probe", UriKind.Relative), Token)).ShouldBe(group);
    }

    [Fact]
    public async Task MapFluentCmsWithoutAddFluentCmsNamesTheMissingCall()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var exception = Should.Throw<FluentCmsConfigurationException>(() => app.MapFluentCms());

        exception.Code.ShouldBe("FCMS1103");
        exception.Message.ShouldContain("AddFluentCms");
    }

    [Theory]
    [InlineData("FluentCms:Query:MaxPageSize", "0", "FCMS1101")]
    [InlineData("FluentCms:Query:DefaultPageSize", "500", "FCMS1101")]
    [InlineData("FluentCms:Query:MaxOffset", "-1", "FCMS1101")]
    [InlineData("FluentCms:Jobs:PollInterval", "-00:00:05", "FCMS2506")]
    [InlineData("FluentCms:Events:Outbox:BatchSize", "0", "FCMS2603")]
    [InlineData("FluentCms:Events:Outbox:OrphanAfter", "00:00:00", "FCMS2603")]
    [InlineData("FluentCms:Events:Outbox:OrphanAfter", "36501.00:00:00", "FCMS2603")]
    [InlineData("FluentCms:Events:Outbox:FailedRetention", "-1.00:00:00", "FCMS2603")]
    [InlineData("FluentCms:Events:Outbox:PruneBatchSize", "1001", "FCMS2603")]
    [InlineData("FluentCms:Events:Handlers:Timeout", "00:00:00", "FCMS1101")]
    [InlineData("FluentCms:Events:Handlers:SlowThreshold", "-00:00:01", "FCMS1101")]
    [InlineData("FluentCms:Activity:PruneSchedule", "never", "FCMS2701")]
    [InlineData("FluentCms:Security:Cors:Delivery:AllowedOrigins:0", "*", "FCMS1101")]
    [InlineData("FluentCms:Security:Cors:Management:AllowedOrigins:0", "https://*.example.com", "FCMS1101")]
    [InlineData("FluentCms:Database:SchemaSync", "7", "FCMS1304")]
    [InlineData("FluentCms:Users:Password:RequiredLength", "0", "FCMS3001")]
    [InlineData("FluentCms:Users:Lockout:MaxFailedAccessAttempts", "0", "FCMS3001")]
    [InlineData("FluentCms:Users:InvitationLifetime", "-1.00:00:00", "FCMS3001")]
    [InlineData("FluentCms:Users:PasswordResetLifetime", "00:00:00", "FCMS3001")]
    [InlineData("FluentCms:ApiKeys:RotationOverlap", "-00:00:01", "FCMS3001")]
    [InlineData("FluentCms:ApiKeys:RotationOverlap", "31.00:00:00", "FCMS3001")]
    [InlineData("FluentCms:Idempotency:Window", "00:00:00", "FCMS1101")]
    [InlineData("FluentCms:Idempotency:Window", "31.00:00:00", "FCMS1101")]
    [InlineData("FluentCms:Idempotency:InProgressTimeout", "1.00:00:01", "FCMS1101")]
    [InlineData("FluentCms:Content:MaxItems", "0", "FCMS1101")]
    [InlineData("FluentCms:Content:HookTimeout", "00:00:00", "FCMS1101")]
    [InlineData("FluentCms:Content:HookTimeout", "1.00:00:01", "FCMS1101")]
    [InlineData("FluentCms:Content:SlowHookThreshold", "-00:00:01", "FCMS1101")]
    [InlineData("FluentCms:Security:Cors:Members:AllowedOrigins:0", "https://*.example.com", "FCMS1101")]
    [InlineData("FluentCms:Members:Password:RequiredLength", "0", "FCMS3160")]
    [InlineData("FluentCms:Members:Lockout:MaxFailedAccessAttempts", "0", "FCMS3160")]
    [InlineData("FluentCms:Members:EmailVerificationLifetime", "00:00:00", "FCMS3160")]
    [InlineData("FluentCms:Members:PasswordResetLifetime", "-01:00:00", "FCMS3160")]
    [InlineData("FluentCms:Members:UnverifiedMemberLifetime", "01:00:00", "FCMS3160")]
    [InlineData("FluentCms:Members:Links:VerifyEmail", "https://app.example.com/verify?email={email}", "FCMS3160")]
    [InlineData("FluentCms:Members:Links:ResetPassword", "/reset?email={email}&token={token}", "FCMS3160")]
    [InlineData("FluentCms:Clients:Applications:app:AllowDirectLogin", "true", "FCMS3021")]
    [InlineData("FluentCms:PreviewTokens:MaximumLifetime", "2.00:00:00", "FCMS3001")]
    [InlineData("FluentCms:PreviewTokens:DefaultLifetime", "00:10:30", "FCMS3001")]
    [InlineData("FluentCms:PreviewTokens:DefaultLifetime", "02:00:00", "FCMS3001")]
    [InlineData("FluentCms:PreviewTokens:DefaultLifetime", "00:00:30", "FCMS3001")]
    public async Task AnInvalidOptionStopsTheHostAndNamesTheKey(string key, string value, string code)
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?> { [key] = value });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe(code);
        exception.Message.ShouldStartWith($"{code}: {key}");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    // UseFluentCms reads the proxy options while the pipeline is built, so the host is refused before it starts.
    [Theory]
    [InlineData("FluentCms:ReverseProxy:PathBase", "cms")]
    [InlineData("FluentCms:ReverseProxy:PathBase", "/cms/")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:KnownProxies:0", "proxy")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:KnownProxies:0", "1")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:KnownProxies:0", "10.1")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:KnownProxies:0", "010.0.0.1")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:KnownNetworks:0", "10/8")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:KnownNetworks:0", "10.0.0.0/+8")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:KnownNetworks:0", "10.0.0.0")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:KnownNetworks:0", "10.0.0.0/33")]
    [InlineData("FluentCms:ReverseProxy:ForwardedHeaders:ForwardLimit", "0")]
    [InlineData("FluentCms:ReverseProxy:HttpsRedirection:Port", "0")]
    [InlineData("FluentCms:ReverseProxy:Hsts:MaxAge", "00:00:00")]
    public void AnInvalidReverseProxyOptionRefusesTheHostAndNamesTheKey(string key, string value)
    {
        var exception = Should.Throw<FluentCmsConfigurationException>(() => _host.Build(Environments.Production, new Dictionary<string, string?> { [key] = value }));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldStartWith($"FCMS1101: {key}");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    [Theory]
    [InlineData("FluentCms:Database:MigrateOnStartup", "maybe", "FCMS1304")]
    [InlineData("FluentCms:Database:SchemaSync", "Sometimes", "FCMS1304")]
    [InlineData("FluentCms:Jobs:PollInterval", "soon", "FCMS2506")]
    [InlineData("FluentCms:Security:AllowGeneratedSigningKey", "yes", "FCMS1101")]
    [InlineData("FluentCms:Security:AllowRequestOrigin", "yes", "FCMS1101")]
    [InlineData("FluentCms:Security:AllowUnencryptedKeyRing", "yes", "FCMS1101")]
    [InlineData("FluentCms:Security:KeyRotation:Enabled", "yes", "FCMS1101")]
    [InlineData("FluentCms:Security:KeyRotation:Interval", "quarterly", "FCMS1101")]
    public async Task AValueThatDoesNotBindStopsTheHostAndNamesTheKey(string key, string value, string code)
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?> { [key] = value });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe(code);
        exception.Message.ShouldContain(key);
    }

    // A new key is published before it signs for at least the 10 minutes in which every instance reads it, a key signs for
    // longer than that, and it is accepted for at least the longest lifetime of a token after its successor signs.
    [Theory]
    [InlineData("PublishLead", "00:09:59")]
    [InlineData("Interval", "1.00:00:00")]
    [InlineData("Overlap", "00:00:00")]
    [InlineData("Overlap", "14.00:05:29")]
    public async Task AKeyRotationOptionOutOfItsRangeStopsTheHost(string option, string value)
    {
        var key = $"FluentCms:Security:KeyRotation:{option}";
        var app = _host.Build(Environments.Production, new Dictionary<string, string?> { [key] = value });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1101");
        exception.Message.ShouldStartWith($"FCMS1101: {key} is {value}");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    [Fact]
    public async Task TheKeyRotationOptionsBindAndTheShortestOverlapIsTheLongestLifetimeOfAToken()
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?>
        {
            ["FluentCms:Security:KeyRotation:Enabled"] = "true",
            ["FluentCms:Security:KeyRotation:Interval"] = "30.00:00:00",
            ["FluentCms:Security:KeyRotation:PublishLead"] = "00:10:00",
            ["FluentCms:Security:KeyRotation:Overlap"] = "14.00:05:30",
        });

        await app.StartAsync(Token);

        var rotation = app.Services.GetRequiredService<SecurityOptions>().KeyRotation;
        rotation.Enabled.ShouldBeTrue();
        rotation.Interval.ShouldBe(TimeSpan.FromDays(30));
        rotation.PublishLead.ShouldBe(TimeSpan.FromMinutes(10));
        rotation.Overlap.ShouldBe(TimeSpan.FromDays(14) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void RoutineRotationIsOffByDefault()
    {
        var rotation = _host.Build(Environments.Development).Services.GetRequiredService<SecurityOptions>().KeyRotation;

        rotation.Enabled.ShouldBeFalse();
        rotation.Interval.ShouldBe(TimeSpan.FromDays(90));
        rotation.PublishLead.ShouldBe(TimeSpan.FromDays(1));
        rotation.Overlap.ShouldBeNull();
    }

    // The report is where an operator who accepted a generated key learns that it never changes.
    [Theory]
    [InlineData("Production", null, true, true)]
    [InlineData("Production", "true", true, false)]
    [InlineData("Production", null, false, false)]
    [InlineData("Development", null, true, false)]
    public async Task AGeneratedKeyThatIsNeverRotatedIsReportedOutsideDevelopment(string environment, string? enabled, bool generated, bool listed)
    {
        var app = _host.Build(
            environment,
            new Dictionary<string, string?> { ["FluentCms:Security:AllowGeneratedSigningKey"] = "true", ["FluentCms:Security:KeyRotation:Enabled"] = enabled },
            builder => builder.Services.AddSingleton<ISigningKeyStatus>(new SigningKey(generated)));

        await app.StartAsync(Token);

        var insecure = app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults;
        insecure.Any(item => item.Id == "signing-key-rotation-off").ShouldBe(listed);
        if (listed)
        {
            insecure.Single(item => item.Id == "signing-key-rotation-off").Message.ShouldContain("FluentCms:Security:KeyRotation:Enabled");
        }
    }

    [Fact]
    public void AKeyTheConfigurationSetsWinsOverTheCodeAndTheRestKeepsTheCode()
    {
        var app = _host.Build(
            Environments.Development,
            new Dictionary<string, string?> { ["FluentCms:Jobs:DefaultMaxAttempts"] = "9", ["FluentCms:Jobs:Queues:reports"] = "3" },
            cms: cms =>
            {
                cms.Jobs.Options.DefaultMaxAttempts = 7;
                cms.Jobs.Options.PollInterval = TimeSpan.FromSeconds(2);
            });

        var jobs = app.Services.GetRequiredService<JobRunnerOptions>();
        jobs.DefaultMaxAttempts.ShouldBe(9);
        jobs.PollInterval.ShouldBe(TimeSpan.FromSeconds(2));
        jobs.Queues["reports"].ShouldBe(3);
        jobs.Queues[JobRunnerOptions.DefaultQueue].ShouldBe(2);
    }

    [Fact]
    public void AnEmptyValueTurnsTheRetentionOfFailuresOff()
    {
        var app = _host.Build(
            Environments.Development,
            new Dictionary<string, string?> { ["FluentCms:Jobs:DeadLetteredRetention"] = "", ["FluentCms:Events:Outbox:FailedRetention"] = "" });

        app.Services.GetRequiredService<JobRunnerOptions>().DeadLetteredRetention.ShouldBeNull();
        app.Services.GetRequiredService<OutboxOptions>().FailedRetention.ShouldBeNull();
    }

    [Fact]
    public void ThePasswordPolicyAndLockoutOfAdminUsersComeFromTheConfiguration()
    {
        var app = _host.Build(
            Environments.Development,
            new Dictionary<string, string?> { ["FluentCms:Users:Password:RequiredLength"] = "20", ["FluentCms:Users:Lockout:MaxFailedAccessAttempts"] = "3" },
            cms: cms =>
            {
                cms.Users.InvitationLifetime = TimeSpan.FromDays(2);
                cms.Users.PasswordResetLifetime = TimeSpan.FromMinutes(20);
            });

        var identity = app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        identity.Password.RequiredLength.ShouldBe(20);
        identity.Password.RequireDigit.ShouldBeTrue();
        identity.Lockout.MaxFailedAccessAttempts.ShouldBe(3);
        identity.User.RequireUniqueEmail.ShouldBeTrue();
        app.Services.GetRequiredService<AdminUserOptions>().InvitationLifetime.ShouldBe(TimeSpan.FromDays(2));
        identity.Tokens.PasswordResetTokenProvider.ShouldBe(PasswordResetTokenProvider.ProviderName);
        app.Services.GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value.TokenLifespan.ShouldBe(TimeSpan.FromMinutes(20));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Qa")]
    public async Task EveryEnvironmentButDevelopmentRefusesAGeneratedSigningKeyBeforeTouchingTheDatabase(string environment)
    {
        var app = _host.Build(environment, configure: GeneratedSigningKey);

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1102");
        exception.Message.ShouldContain("FluentCms:Security:AllowGeneratedSigningKey");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    [Fact]
    public async Task AllowGeneratedSigningKeyLetsProductionStart()
    {
        var app = _host.Build(
            Environments.Production,
            new Dictionary<string, string?> { ["FluentCms:Security:AllowGeneratedSigningKey"] = "true" },
            GeneratedSigningKey);

        await app.StartAsync(Token);

        File.Exists(_host.DatabaseFile).ShouldBeTrue();
    }

    [Fact]
    public async Task DevelopmentStartsWithAGeneratedSigningKey()
    {
        var app = _host.Build(Environments.Development, configure: GeneratedSigningKey);

        await app.StartAsync(Token);
    }

    [Fact]
    public async Task ProductionStartsWhenTheSigningKeyIsConfigured()
    {
        var app = _host.Build(Environments.Production, configure: builder => builder.Services.AddSingleton<ISigningKeyStatus>(new SigningKey(IsGenerated: false)));

        await app.StartAsync(Token);
    }

    [Fact]
    public void CookiesAreSecureByDefaultAndAHostCanStillChangeThem()
    {
        var app = _host.Build(Environments.Production, configure: builder =>
        {
            builder.Services.AddAuthentication().AddCookie().AddCookie("relaxed");
            builder.Services.Configure<CookieAuthenticationOptions>("relaxed", options => options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
        });

        var cookies = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var standard = cookies.Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie;
        standard.SecurePolicy.ShouldBe(CookieSecurePolicy.Always);
        standard.HttpOnly.ShouldBeTrue();
        standard.SameSite.ShouldBe(SameSiteMode.Lax);
        cookies.Get("relaxed").Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.SameAsRequest);

        app.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy.ShouldBe(CookieSecurePolicy.Always);
    }

    [Fact]
    public void NoBrowserOriginIsAllowedByDefault()
    {
        var app = _host.Build(Environments.Production);

        var cors = app.Services.GetRequiredService<SecurityOptions>().Cors;
        cors.Delivery.AllowedOrigins.ShouldBeEmpty();
        cors.Management.AllowedOrigins.ShouldBeEmpty();
    }

    [Fact]
    public void ListedOriginsBindOnceHoweverTheOptionsAreRead()
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?>
        {
            ["FluentCms:Security:Cors:Delivery:AllowedOrigins:0"] = "https://www.example.com",
            ["FluentCms:Security:Cors:Delivery:AllowedOrigins:1"] = "http://localhost:3000",
        });

        app.Services.GetRequiredService<IOptionsMonitor<SecurityOptions>>().CurrentValue.Cors.Delivery.AllowedOrigins.Count.ShouldBe(2);
        app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.Cors.Delivery.AllowedOrigins
            .ShouldBe(["https://www.example.com", "http://localhost:3000"]);
    }

    private static void GeneratedSigningKey(WebApplicationBuilder builder) =>
        builder.Services.AddSingleton<ISigningKeyStatus>(new SigningKey(IsGenerated: true));

    private sealed record SigningKey(bool IsGenerated) : ISigningKeyStatus;
}

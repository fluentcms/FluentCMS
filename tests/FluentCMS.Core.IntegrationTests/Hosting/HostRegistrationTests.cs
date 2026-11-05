using System.Net;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

// The one test that sets a process environment variable lives here with every other test whose host reads them, and the
// tests of a class run one at a time.
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
        using var response = await app.GetTestClient().GetAsync(new Uri("/api/delivery/v1/article", UriKind.Relative), Token);
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
            configure: builder =>
            {
                foreach (var each in Enum.GetValues<FluentCmsEndpointGroup>())
                {
                    builder.Services.AddFluentCmsEndpoints(each, routes => routes.MapGet("/probe", () => each.ToString()));
                }
            },
            pipeline: app =>
            {
                app.UsePathBase("/cms");
                app.UseRouting();
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
    [InlineData("FluentCms:Jobs:PollInterval", "-00:00:05", "FCMS2506")]
    [InlineData("FluentCms:Events:Outbox:BatchSize", "0", "FCMS2603")]
    [InlineData("FluentCms:Activity:PruneSchedule", "never", "FCMS2701")]
    [InlineData("FluentCms:Security:Cors:Delivery:AllowedOrigins:0", "*", "FCMS1101")]
    [InlineData("FluentCms:Security:Cors:Management:AllowedOrigins:0", "https://*.example.com", "FCMS1101")]
    [InlineData("FluentCms:Database:SchemaSync", "7", "FCMS1304")]
    public async Task AnInvalidOptionStopsTheHostAndNamesTheKey(string key, string value, string code)
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?> { [key] = value });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe(code);
        exception.Message.ShouldStartWith($"{code}: {key}");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    [Theory]
    [InlineData("FluentCms:Database:MigrateOnStartup", "maybe", "FCMS1304")]
    [InlineData("FluentCms:Database:SchemaSync", "Sometimes", "FCMS1304")]
    [InlineData("FluentCms:Jobs:PollInterval", "soon", "FCMS2506")]
    [InlineData("FluentCms:Security:AllowGeneratedSigningKey", "yes", "FCMS1101")]
    public async Task AValueThatDoesNotBindStopsTheHostAndNamesTheKey(string key, string value, string code)
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?> { [key] = value });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe(code);
        exception.Message.ShouldContain(key);
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
    public async Task AnEnvironmentVariableReachesTheOptionsThroughTheDoubleUnderscore()
    {
        const string Variable = "FluentCms__Database__MigrateOnStartup";
        Environment.SetEnvironmentVariable(Variable, "false");
        try
        {
            var app = _host.Build(Environments.Production);

            app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup.ShouldBe(false);

            // Nothing migrates a new database at start, so the system stream is pending.
            var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));
            exception.Code.ShouldBe("FCMS1305");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    [Fact]
    public async Task ProductionRefusesAGeneratedSigningKeyBeforeTouchingTheDatabase()
    {
        var app = _host.Build(Environments.Production, configure: GeneratedSigningKey);

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

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task OnlyProductionRefusesAGeneratedSigningKey(string environment)
    {
        var app = _host.Build(environment, configure: GeneratedSigningKey);

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

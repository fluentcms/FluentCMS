using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Core.Storage;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Diagnostics;

// A report says what its host's set-up is and the endpoint only reads it, so the tests that do not set a host up of their own
// share one, and those of one web host set-up share that.
public sealed class StartupReportTests(CmsWebHosts webHosts, NoteHostFixture fixture) : IClassFixture<CmsWebHosts>, IClassFixture<NoteHostFixture>
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _shared = fixture.Host;

    [Fact]
    public async Task TheReportIsLoggedOncePerStartWithTheProviderMigrationsAndInsecureDefaults()
    {
        var logs = new LogCapture();
        await using var host = new FluentCmsTestHost().WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));

        _ = host.Services;

        var reports = logs.Entries.Where(entry => entry.EventId.Id == StartupReportEvents.Report).ToList();
        var report = reports.ShouldHaveSingleItem();
        report.Level.ShouldBe(LogLevel.Information);
        report.Category.ShouldBe(typeof(StartupReportService).FullName);

        var logged = JsonNode.Parse((string)report.State["Report"]!)!;
        logged["environment"]!.GetValue<string>().ShouldBe("Development");
        logged["dataProvider"]!["provider"]!.GetValue<string>().ShouldBe("SQLite");
        logged["dataProvider"]!["providerVersion"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        // The Blog sample, which this project references, brings the media plugin, and discovery finds it.
        logged["plugins"]!.AsArray().Select(plugin => plugin!["name"]!.GetValue<string>()).ShouldBe(["media"]);
        logged["assemblies"]!.AsArray().Select(assembly => assembly!["name"]!.GetValue<string>()).ShouldContain("FluentCMS.Core");

        var runner = host.Services.GetRequiredService<MigrationRunner>();
        var pending = await runner.GetPendingMigrationsAsync(Token);
        logged["pendingMigrations"]!.AsArray().Count.ShouldBe(pending.Count);
        ((int)report.State["PendingMigrations"]!).ShouldBe(pending.Count);

        var insecure = logged["insecureDefaults"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToList();
        insecure.ShouldContain("development-email-sender");
        // The generated signing key is named by the refusal it would be in Production.
        insecure.ShouldContain("FCMS1102");
        insecure.ShouldContain("data-protection-keys-unencrypted");
        var warnings = logs.Entries.Where(entry => entry.EventId.Id == StartupReportEvents.InsecureDefault).ToList();
        warnings.ShouldAllBe(entry => entry.Level == LogLevel.Warning);
        warnings.Select(entry => (string)entry.State["Id"]!).ShouldBe(insecure, ignoreOrder: true);
    }

    [Fact]
    public async Task AnEncryptedKeyRingAndASigningCertificateClearTheirEntries()
    {
        await using var host = new FluentCmsTestHost().WithServices(services =>
        {
            services.Configure<KeyManagementOptions>(options => options.XmlEncryptor = new NullXmlEncryptor());
            services.AddSingleton<ISigningKeyStatus>(new CertificateKey());
        });

        var insecure = host.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ToList();

        insecure.ShouldNotContain("data-protection-keys-unencrypted");
        insecure.ShouldNotContain("FCMS1102");
        insecure.ShouldNotContain("generated-signing-key");
    }

    [Fact]
    public async Task ProductionNamesTheDefaultsItCouldNotRefuse()
    {
        var app = await StartAsync(Environments.Production);

        var report = app.Services.GetRequiredService<StartupReportHolder>().Current!;

        report.Environment.ShouldBe("Production");
        report.InsecureDefaults.Select(item => item.Id).ShouldContain("https-redirection-off");
        report.InsecureDefaults.Select(item => item.Id).ShouldContain("allowed-hosts-any");
        report.InsecureDefaults.Select(item => item.Id).ShouldNotContain("development-email-sender");
        report.Schema.Hash.ShouldNotBeNullOrWhiteSpace();
        report.Schema.Drifted.ShouldBeFalse();
    }

    [Fact]
    public async Task TurningTheRedirectOnClearsItsEntry()
    {
        var app = await StartAsync(Environments.Production, new Dictionary<string, string?> { ["FluentCms:ReverseProxy:HttpsRedirection:Enabled"] = "true", ["FluentCms:ReverseProxy:HttpsRedirection:Port"] = "443" });

        app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldNotContain("https-redirection-off");
    }

    [Theory]
    [InlineData("cms.example.com", false)]
    [InlineData("cms.example.com;localhost", false)]
    [InlineData("*", true)]
    [InlineData("cms.example.com;*", true)]
    public async Task AllowedHostsIsReportedInProductionOnlyWhenItAcceptsAnyHost(string allowedHosts, bool reported)
    {
        var app = await StartAsync(Environments.Production, new Dictionary<string, string?> { ["AllowedHosts"] = allowedHosts });

        var insecure = app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ToList();

        if (reported)
        {
            insecure.ShouldContain("allowed-hosts-any");
        }
        else
        {
            insecure.ShouldNotContain("allowed-hosts-any");
        }
    }

    [Fact]
    public async Task AllowedHostsIsNotReportedOutsideProduction()
    {
        var app = await StartAsync(Environments.Staging);

        app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldNotContain("allowed-hosts-any");
    }

    [Fact]
    public async Task AMissingPublicOriginIsReportedInProduction()
    {
        var app = await StartAsync(Environments.Production, new Dictionary<string, string?> { ["AllowedHosts"] = "cms.example.com" });

        var insecure = app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Single(item => item.Id == "public-origin-unset");
        insecure.Message.ShouldContain("FluentCms:PublicOrigin");
    }

    [Fact]
    public async Task APublicOriginClearsItsEntry()
    {
        var app = await StartAsync(Environments.Production, new Dictionary<string, string?> { ["FluentCms:PublicOrigin"] = "https://cms.example.com" });

        app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldNotContain("public-origin-unset");
    }

    [Fact]
    public async Task AMissingPublicOriginIsNotReportedOutsideProduction()
    {
        var app = await StartAsync(Environments.Staging);

        app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldNotContain("public-origin-unset");
    }

    [Fact]
    public async Task ForwardedHeadersTrustedFromEveryAddressAreReported()
    {
        var app = await StartAsync(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["FluentCms:ReverseProxy:ForwardedHeaders:Enabled"] = "true",
                ["FluentCms:ReverseProxy:ForwardedHeaders:KnownNetworks:0"] = "0.0.0.0/0",
            });

        app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldContain("forwarded-headers-trust-any");
    }

    [Fact]
    public async Task TheReportHoldsNoConnectionStringOrKey()
    {
        var host = _shared;
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);

        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotContain("fluentcms-test-");
        body.ShouldNotContain("Data Source", Case.Insensitive);
        body.ShouldNotContain(key.Secret);
        body.ShouldNotContain("fcms_mk_");
    }

    [Fact]
    public async Task DiagnosticsIsRefusedWithoutACredentialAndWithoutThePermission()
    {
        var host = _shared;
        var editor = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);

        using var anonymous = await host.CreateClient().GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        using var withoutPermission = await host.AsKey(editor).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withoutPermission.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(withoutPermission)).ShouldBe("FCMS3103");
    }

    [Fact]
    public async Task DiagnosticsReturnsTheLoggedReportToAKeyWhoseRoleHasThePermission()
    {
        var logs = new LogCapture();
        await using var host = new FluentCmsTestHost().WithServices(services => services.AddLogging(logging => logging.AddProvider(logs)));
        await host.CreateRoleAsync("operator", [CorePermissions.SystemRead], Token);
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, "operator", cancellationToken: Token);

        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var served = JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!;
        var logged = JsonNode.Parse((string)logs.Entries.Single(entry => entry.EventId.Id == StartupReportEvents.Report).State["Report"]!)!;
        JsonNode.DeepEquals(served, logged).ShouldBeTrue();
    }

    [Fact]
    public async Task ADeliveryKeyDoesNotReachDiagnosticsWhateverItsRoleGrants()
    {
        var host = _shared;
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.SuperAdminName, cancellationToken: Token);

        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARoleOfOneSiteDoesNotReachDiagnostics()
    {
        var host = _shared;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            var role = new FluentCMS.Core.Storage.Entities.Role { Id = Guid.CreateVersion7(), Name = "site operator", SiteId = FluentCMS.Core.Sites.DefaultSite.Id };
            context.Set<FluentCMS.Core.Storage.Entities.Role>().Add(role);
            context.Set<FluentCMS.Core.Storage.Entities.RoleGrant>().Add(new FluentCMS.Core.Storage.Entities.RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, Permission = CorePermissions.SystemRead });
            await context.SaveChangesAsync(Token);
        }

        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, "site operator", cancellationToken: Token);

        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AKeyBoundToASiteDoesNotReachDiagnostics()
    {
        var host = _shared;
        var role = await host.CreateRoleAsync("operator", [CorePermissions.SystemRead], Token);
        string secret;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(FluentCMS.Core.Sites.DefaultSite.Id));
            var created = await scope.ServiceProvider.GetRequiredService<FluentCMS.Core.Identity.ApiKeys.ApiKeyService>().CreateAsync(
                new FluentCMS.Core.Identity.ApiKeys.ApiKeyRequest("bound", FluentCMS.Core.Identity.ApiKeys.ApiKeyClass.Management, role) { SiteIds = [FluentCMS.Core.Sites.DefaultSite.Id] },
                Token);
            secret = created.Secret;
        }

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", secret);
        using var response = await client.GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AReportThatCannotBeMadeLeavesADegradedOneAndNothingSecretInTheLog()
    {
        var logs = new LogCapture();
        await using var host = new FluentCmsTestHost().WithServices(services =>
        {
            services.AddLogging(logging => logging.AddProvider(logs));
            services.AddSingleton<IProductionStartupCheck>(new ThrowingCheck());
        });
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);

        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var served = JsonNode.Parse(body)!;
        served["failure"]!.GetValue<string>().ShouldBe(nameof(InvalidOperationException));
        served["dataProvider"]!["provider"]!.GetValue<string>().ShouldBe("SQLite");
        served["pendingMigrations"]!.AsArray().ShouldBeEmpty();
        body.ShouldNotContain("hunter2");

        var warning = logs.Entries.Single(entry => entry.EventId.Id == StartupReportEvents.NotMade);
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Exception.ShouldBeNull();
        warning.Message.ShouldContain(nameof(InvalidOperationException));
        warning.Message.ShouldNotContain("hunter2");
        logs.Entries.ShouldNotContain(entry => entry.EventId.Id == StartupReportEvents.Report);
    }

    [Fact]
    public async Task DiagnosticsIs503BeforeTheReportExists()
    {
        var host = _shared;
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);
        var holder = host.Services.GetRequiredService<StartupReportHolder>();
        var report = holder.Current;
        holder.Set(null);

        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        holder.Set(report);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await Code(response)).ShouldBe("FCMS3106");
    }

    [Fact]
    public async Task DiagnosticsChecksThePermissionBeforeItAdmitsThereIsNoReport()
    {
        var host = _shared;
        var editor = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        var holder = host.Services.GetRequiredService<StartupReportHolder>();
        var report = holder.Current;
        holder.Set(null);

        using var response = await host.AsKey(editor).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);
        holder.Set(report);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void SystemReadIsInTheCatalogue()
    {
        CorePermissions.SystemRead.ShouldBe("system.read");
        CorePermissions.Fixed.ShouldContain(CorePermissions.SystemRead);
    }

    private Task<WebApplication> StartAsync(string environment, Dictionary<string, string?>? settings = null) =>
        webHosts.StartAsync(CmsWebHosts.SetUp(environment, settings), async host =>
        {
            var app = host.Build(environment, settings);
            await app.StartAsync(Token);
            return app;
        });

    private sealed class ThrowingCheck : IProductionStartupCheck
    {
        public StartupRefusal? Check() => throw new InvalidOperationException("Data Source=cms.db; password=hunter2");
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("code").GetString();

    private sealed class CertificateKey : ISigningKeyStatus
    {
        public bool IsGenerated => false;
    }
}

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Core.Storage;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Diagnostics;

[Xunit.Collection(ProcessEnvironment.Collection)]
public sealed class StartupReportTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

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
        logged["plugins"]!.AsArray().ShouldBeEmpty();
        logged["assemblies"]!.AsArray().Select(assembly => assembly!["name"]!.GetValue<string>()).ShouldContain("FluentCMS.Core");

        var runner = host.Services.GetRequiredService<MigrationRunner>();
        var pending = await runner.GetPendingMigrationsAsync(Token);
        logged["pendingMigrations"]!.AsArray().Count.ShouldBe(pending.Count);
        ((int)report.State["PendingMigrations"]!).ShouldBe(pending.Count);

        var insecure = logged["insecureDefaults"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).ToList();
        insecure.ShouldContain("development-email-sender");
        var warnings = logs.Entries.Where(entry => entry.EventId.Id == StartupReportEvents.InsecureDefault).ToList();
        warnings.ShouldAllBe(entry => entry.Level == LogLevel.Warning);
        warnings.Select(entry => (string)entry.State["Id"]!).ShouldBe(insecure, ignoreOrder: true);
    }

    [Fact]
    public async Task ProductionNamesTheDefaultsItCouldNotRefuse()
    {
        var app = _host.Build(Environments.Production);
        await app.StartAsync(Token);

        var report = app.Services.GetRequiredService<StartupReportHolder>().Report;

        report.Environment.ShouldBe("Production");
        report.InsecureDefaults.Select(item => item.Id).ShouldContain("https-redirection-off");
        report.InsecureDefaults.Select(item => item.Id).ShouldNotContain("development-email-sender");
        report.Schema.Hash.ShouldNotBeNullOrWhiteSpace();
        report.Schema.Drifted.ShouldBeFalse();
    }

    [Fact]
    public async Task TurningTheRedirectOnClearsItsEntry()
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?> { ["FluentCms:ReverseProxy:HttpsRedirection:Enabled"] = "true", ["FluentCms:ReverseProxy:HttpsRedirection:Port"] = "443" });
        await app.StartAsync(Token);

        app.Services.GetRequiredService<StartupReportHolder>().Report.InsecureDefaults.Select(item => item.Id).ShouldNotContain("https-redirection-off");
    }

    [Fact]
    public async Task ForwardedHeadersTrustedFromEveryAddressAreReported()
    {
        var app = _host.Build(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["FluentCms:ReverseProxy:ForwardedHeaders:Enabled"] = "true",
                ["FluentCms:ReverseProxy:ForwardedHeaders:KnownNetworks:0"] = "0.0.0.0/0",
            });
        await app.StartAsync(Token);

        app.Services.GetRequiredService<StartupReportHolder>().Report.InsecureDefaults.Select(item => item.Id).ShouldContain("forwarded-headers-trust-any");
    }

    [Fact]
    public async Task TheReportHoldsNoConnectionStringOrKey()
    {
        await using var host = new FluentCmsTestHost();
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
        await using var host = new FluentCmsTestHost();
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
        await using var host = new FluentCmsTestHost();
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Delivery, BuiltInRoles.SuperAdminName, cancellationToken: Token);

        using var response = await host.AsKey(key).GetAsync(new Uri("/api/management/v1/system/diagnostics", UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARoleOfOneSiteDoesNotReachDiagnostics()
    {
        await using var host = new FluentCmsTestHost();
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
        await using var host = new FluentCmsTestHost();
        var role = await host.CreateRoleAsync("operator", [CorePermissions.SystemRead], Token);
        string secret;
        await using (var scope = host.Services.CreateAsyncScope())
        {
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
    public void SystemReadIsInTheCatalogue()
    {
        CorePermissions.SystemRead.ShouldBe("system.read");
        CorePermissions.Fixed.ShouldContain(CorePermissions.SystemRead);
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("code").GetString();
}

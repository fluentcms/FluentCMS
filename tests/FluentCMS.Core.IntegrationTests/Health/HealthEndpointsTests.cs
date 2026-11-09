using System.Data.Common;
using System.Net;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Health;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.IntegrationTests.Testing;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Xunit;
using TestKeyClass = FluentCMS.Testing.ApiKeyClass;

namespace FluentCMS.Core.IntegrationTests.Health;

public sealed class HealthEndpointsTests(NoteHostFixture fixture) : IClassFixture<NoteHostFixture>
{
    private const string Detailed = "/api/management/v1/system/health";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly FluentCmsTestHost _host = fixture.Host;

    [Fact]
    public async Task LiveAndReadyNeedNoAuthenticationAndAreHealthyOnSqlite()
    {
        using var client = _host.CreateClient();

        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync(Token)).ShouldBe("Healthy");
        }
    }

    [Fact]
    public async Task AKeySentToTheProbesIsNeverLookedUp()
    {
        var lookups = new KeyLookups();
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(lookups)));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKeyFormat.Generate(Core.Identity.ApiKeys.ApiKeyClass.Delivery));

        foreach (var path in new[] { "/health/live", "/health/ready" })
        {
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync(Token)).ShouldBe("Healthy");
        }

        lookups.Count.ShouldBe(0);

        // The same key on another route is looked up, so the count sees a lookup when there is one.
        using var refused = await client.GetAsync(new Uri(Detailed, UriKind.Relative), Token);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        lookups.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ReadyIsUnhealthyWhenTheDatabaseIsUnreachableAndLiveStaysHealthy()
    {
        var outage = new Outage();
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .Configure(cms => cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(outage)));
        using var client = host.CreateClient();
        using var before = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The database stops answering under the running host.
        outage.Down = true;

        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Token);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ready.Content.ReadAsStringAsync(Token)).ShouldBe("Unhealthy");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Back up, so that the host stops as it would after an outage.
        outage.Down = false;
    }

    [Fact]
    public async Task ReadyIsUnhealthyWhenTheJobRunnerHeartbeatIsStale()
    {
        // A maintenance turn an hour away cannot beat between the clock moving and the probe.
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .Configure(cms => cms.Jobs.Options.MaintenanceInterval = TimeSpan.FromHours(1));
        using var client = host.CreateClient();
        using var fresh = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
        fresh.StatusCode.ShouldBe(HttpStatusCode.OK);

        host.Time.Advance(host.Services.GetRequiredService<HealthOptions>().JobRunnerHeartbeatTimeout + TimeSpan.FromMinutes(1));

        using var stale = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Token);

        stale.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await stale.Content.ReadAsStringAsync(Token)).ShouldBe("Unhealthy");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnInfiniteHeartbeatLimitLeavesTheJobRunnerOutOfTheCheck()
    {
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .Configure(cms => cms.Jobs.Options.MaintenanceInterval = TimeSpan.FromHours(1))
            .WithServices(services => services.PostConfigure<HealthOptions>(health => health.JobRunnerHeartbeatTimeout = Timeout.InfiniteTimeSpan));
        using var client = host.CreateClient();
        host.Time.Advance(TimeSpan.FromDays(1));

        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheDetailedReportIsRefusedWithoutSystemRead()
    {
        var withoutPermission = await _host.CreateApiKeyAsync(TestKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        var role = await _host.CreateRoleAsync("Operations", [CorePermissions.SystemRead], Token);
        var allowed = await CreateKeyAsync(role, Core.Identity.ApiKeys.ApiKeyClass.Management);
        var delivery = await CreateKeyAsync(role, Core.Identity.ApiKeys.ApiKeyClass.Delivery);
        var siteBound = await CreateKeyAsync(role, Core.Identity.ApiKeys.ApiKeyClass.Management, DefaultSite.Id);
        using var anonymous = _host.CreateClient();

        using var none = await anonymous.GetAsync(new Uri(Detailed, UriKind.Relative), Token);
        using var lacking = await _host.AsKey(withoutPermission).GetAsync(new Uri(Detailed, UriKind.Relative), Token);
        using var deliveryKey = await Client(delivery).GetAsync(new Uri(Detailed, UriKind.Relative), Token);
        using var bound = await Client(siteBound).GetAsync(new Uri(Detailed, UriKind.Relative), Token);
        using var granted = await Client(allowed).GetAsync(new Uri(Detailed, UriKind.Relative), Token);

        none.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        lacking.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        deliveryKey.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        bound.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);

        var report = JsonNode.Parse(await granted.Content.ReadAsStringAsync(Token))!;
        report["status"]!.GetValue<string>().ShouldBe("Healthy");
        var checks = report["checks"]!.AsArray();
        // The Blog sample brings the media plugin into every host here, with its storage check.
        checks.Select(check => check!["name"]!.GetValue<string>()).ShouldBe(["database", "job-runner", "media-storage", "process"]);
        checks.Select(check => check!["durationMs"]!.GetValue<double>()).ShouldAllBe(duration => duration >= 0);
    }

    [Fact]
    public async Task ASuperAdminKeyReadsTheDetailedReport()
    {
        var key = await _host.CreateApiKeyAsync(TestKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);

        using var response = await _host.AsKey(key).GetAsync(new Uri(Detailed, UriKind.Relative), Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AReadyCheckIsAddedInOneCallAndNeitherTheProbeNorTheReportLeaksItsException()
    {
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .WithServices(services => services.AddHealthChecks().AddFluentCmsReadyCheck<LeakyCheck>("leaky"));
        var key = await host.CreateApiKeyAsync(TestKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token);

        using var probe = await host.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
        using var detailed = await host.AsKey(key).GetAsync(new Uri(Detailed, UriKind.Relative), Token);

        probe.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await probe.Content.ReadAsStringAsync(Token)).ShouldBe("Unhealthy");
        detailed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await detailed.Content.ReadAsStringAsync(Token);
        text.ShouldContain("leaky");
        text.ShouldContain("Unhealthy");
        text.ShouldNotContain("hunter2");
    }

    private HttpClient Client(string secret)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("ApiKey", secret);
        return client;
    }

    private async Task<string> CreateKeyAsync(Guid role, Core.Identity.ApiKeys.ApiKeyClass keyClass, params Guid[] sites)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        var keys = scope.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = await keys.CreateAsync(new ApiKeyRequest($"{keyClass} key", keyClass, role) { SiteIds = sites.Length > 0 ? sites : null }, Token);
        return created.Secret;
    }

    private sealed class LeakyCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Server=db.internal;Password=hunter2;");
    }

    // Once down, every connection the system tables open fails as an unreachable SQLite file does.
    private sealed class Outage : DbConnectionInterceptor
    {
        private volatile bool _down;

        public bool Down
        {
            get => _down;
            set => _down = value;
        }

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            Down ? throw Unreachable() : result;

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            Down ? throw Unreachable() : ValueTask.FromResult(result);

        private static SqliteException Unreachable() => new("unable to open database file", 14);
    }

    // Counts the queries of the API key table, which is what an API key lookup reads.
    private sealed class KeyLookups : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command)
        {
            if (command.CommandText.Contains("\"cms_api_key\"", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }
        }
    }
}

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Jobs;
using FluentCMS.Plugins.Sample;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

// Per-site enablement of #2830, through the sample plugin, disabled in a second site and left on in the default one.
public sealed class PluginSiteTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADisabledPluginAnswers404OnItsEndpointsInThatSiteFromTheNextRequestAndKeepsServingAnother()
    {
        await using var host = new FluentCmsTestHost();
        var docs = await CreateSiteAsync(host);
        using var admin = await AdminAsync(host);

        (await StatusAsync(admin, "/sample/greeting", docs)).ShouldBe(HttpStatusCode.OK);
        (await SetStatusAsync(admin, docs, enabled: false)).ShouldBe(HttpStatusCode.OK);

        foreach (var path in new[] { "/sample/greeting", "/api/management/v1/sample/notes" })
        {
            using var refused = await SendAsync(admin, HttpMethod.Get, path, docs);
            refused.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            (await CodeAsync(refused)).ShouldBe("FCMS1550");
            (await StatusAsync(admin, path, DefaultSite.Id)).ShouldBe(HttpStatusCode.OK, path);
        }

        (await SetStatusAsync(admin, docs, enabled: true)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync(admin, "/sample/greeting", docs)).ShouldBe(HttpStatusCode.OK);
        (await host.Services.GetRequiredService<IPluginState>().IsEnabledAsync("sample", new SiteId(docs), Token)).ShouldBeTrue();
    }

    // The caller is checked before the plugin's state, so an anonymous caller learns nothing of where the plugin is off.
    [Fact]
    public async Task AnAnonymousCallerIsRefusedBeforeTheDisabledPluginAnswers404()
    {
        await using var host = new FluentCmsTestHost();
        var docs = await CreateSiteAsync(host);
        using var admin = await AdminAsync(host);
        (await SetStatusAsync(admin, docs, enabled: false)).ShouldBe(HttpStatusCode.OK);
        using var anonymous = host.CreateClient();

        using var greeting = await SendAsync(anonymous, HttpMethod.Get, "/sample/greeting", docs);
        using var banner = await SendAsync(anonymous, HttpMethod.Get, "/sample/banner", docs);

        greeting.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(greeting)).ShouldBe("FCMS3102");
        banner.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(banner)).ShouldBe("FCMS1550");
        (await StatusAsync(anonymous, "/sample/banner", DefaultSite.Id)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InTheDisabledSiteItsHooksHandlersAndJobsDoNotRunWhileOtherSitesAreUnaffected()
    {
        var calls = new PluginCalls();
        await using var host = new FluentCmsTestHost().WithContentTypes<Note>().WithServices(services => services.AddSingleton(calls));
        var docs = await CreateSiteAsync(host);
        await DisableAsync(host, docs);
        (await host.Services.GetRequiredService<IPluginState>().IsEnabledAsync("sample", new SiteId(docs), Token)).ShouldBeFalse();
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);

        await host.As(editor).In(new SiteId(docs)).Content<Note>().CreateAsync(new Note { Title = "in docs" }, Token);
        calls.All.ShouldNotContain("sample.hook");
        await host.As(editor).Content<Note>().CreateAsync(new Note { Title = "in default" }, Token);
        calls.All.Count(call => call == "sample.hook").ShouldBe(1);

        await PublishAsync(host, docs, "raised in docs");
        await PublishAsync(host, DefaultSite.Id, "raised in default");
        calls.All.ShouldNotContain("sample.event:raised in docs");
        calls.All.ShouldContain("sample.event:raised in default");
        (await OutboxAsync(host, "sample.note-added")).ShouldBe([DefaultSite.Id]);

        var scheduler = host.Services.GetRequiredService<IJobScheduler>();
        var skipped = await scheduler.EnqueueAsync<SampleNoteJob, SampleNotePayload>(new SampleNotePayload("job in docs"), new JobOptions { Site = new SiteId(docs) }, Token);
        await RunJobsAsync(host);
        var ran = await scheduler.EnqueueAsync<SampleNoteJob, SampleNotePayload>(new SampleNotePayload("job in default"), new JobOptions { Site = new SiteId(DefaultSite.Id) }, Token);
        await RunJobsAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking();
        (await jobs.SingleAsync(job => job.Id == skipped, Token)).Status.ShouldBe(JobStatus.Succeeded);
        (await jobs.SingleAsync(job => job.Id == ran, Token)).Status.ShouldBe(JobStatus.Succeeded);
        (await scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>().Set<SampleNote>().Select(note => note.Text).ToListAsync(Token))
            .ShouldBe(["job in default"]);
    }

    // Recorded while the plugin was on, a delivery is the plugin's: it waits without using an attempt, and goes once it is on again.
    [Fact]
    public async Task ADeliveryRecordedBeforeThePluginWasDisabledWaitsAndIsDeliveredOnceItIsEnabledAgain()
    {
        var calls = new PluginCalls();
        await using var host = new FluentCmsTestHost().WithServices(services => services.AddSingleton(calls));
        var docs = await CreateSiteAsync(host);
        await PublishAsync(host, docs, "before");
        await DisableAsync(host, docs);

        await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);

        calls.All.ShouldNotContain("sample.outbox:before");
        var waiting = await OutboxRowAsync(host, "sample.note-added");
        (waiting.Status, waiting.Attempts).ShouldBe((OutboxStatus.Pending, 0));
        waiting.AvailableAt.ShouldBeGreaterThan(host.Time.GetUtcNow());

        await EnableAsync(host, docs);
        host.Time.Advance(host.Services.GetRequiredService<OutboxOptions>().MissingHandlerDelay);

        // Moving the clock wakes the job runner too, which may be the one to deliver it.
        var delivered = waiting;
        for (var attempt = 0; attempt < 50 && delivered.Status != OutboxStatus.Delivered; attempt++)
        {
            await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            delivered = await OutboxRowAsync(host, "sample.note-added");
            if (delivered.Status != OutboxStatus.Delivered)
            {
                await Task.Delay(100, Token);
            }
        }

        delivered.Status.ShouldBe(OutboxStatus.Delivered);
        calls.All.ShouldContain("sample.outbox:before");
    }

    [Fact]
    public async Task ThePluginListHasEveryPluginWithItsManifestContributionsAndEnablementPerSite()
    {
        await using var host = new FluentCmsTestHost();
        var docs = await CreateSiteAsync(host);
        using var admin = await AdminAsync(host);
        (await SetStatusAsync(admin, docs, enabled: false)).ShouldBe(HttpStatusCode.OK);

        var sample = (await ListAsync(admin, DefaultSite.Id)).Single(item => item!["name"]!.GetValue<string>() == "sample")!;

        sample["version"]!.GetValue<string>().ShouldBe("1.2.0");
        sample["sdkRange"]!.GetValue<string>().ShouldBe("[1.0.0,2.0.0)");
        sample["dependsOn"]!.AsArray().ShouldBeEmpty();
        sample["enabled"]!.GetValue<bool>().ShouldBeTrue();
        var contributions = sample["contributions"]!;
        Strings(contributions["fieldTypes"]!).ShouldBe([ExcerptFieldType.TypeName]);
        Strings(contributions["permissions"]!).ShouldBe([SamplePermissions.NotesManage]);
        Strings(contributions["jobs"]!).ShouldBe([SampleNoteJob.Type]);
        contributions["endpoints"]!.AsArray().Select(endpoint => $"{endpoint!["method"]!.GetValue<string>()} {endpoint["route"]!.GetValue<string>()}")
            .ShouldBe(["GET /api/delivery/v1/sample/fresh", "GET /api/delivery/v1/sample/greeting", "GET /api/management/v1/sample/notes", "POST /api/management/v1/sample/notes", "GET /sample/banner", "GET /sample/greeting"]);
        sample["sites"]!.AsArray().Select(site => (site!["siteId"]!.GetValue<Guid>(), site["slug"]!.GetValue<string>(), site["enabled"]!.GetValue<bool>()))
            .ShouldBe([(DefaultSite.Id, DefaultSite.Slug, true), (docs, "docs", false)]);

        var inDocs = (await ListAsync(admin, docs)).Single(item => item!["name"]!.GetValue<string>() == "sample")!;
        inDocs["enabled"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task ACallerWithoutPluginsManageReadsTheListForItsSiteOnly()
    {
        await using var host = new FluentCmsTestHost();
        var docs = await CreateSiteAsync(host);
        await DisableAsync(host, docs);
        var key = await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.EditorName, cancellationToken: Token);
        using var editor = host.AsKey(key);

        var sample = (await ListAsync(editor, docs)).Single(item => item!["name"]!.GetValue<string>() == "sample")!;

        sample["enabled"]!.GetValue<bool>().ShouldBeFalse();
        sample["sites"]!.AsArray().ShouldBeEmpty();
        (await SetStatusAsync(editor, docs, enabled: true)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EnablingAndDisablingNeedPluginsManageForTheSiteAndRaiseTheirEventWithAnActivityRow()
    {
        await using var host = new FluentCmsTestHost();
        var docs = await CreateSiteAsync(host);
        using var admin = await AdminAsync(host);
        using var role = await admin.PostAsync(
            new Uri("/api/management/v1/roles", UriKind.Relative),
            Json($$"""{ "name": "docs-plugins", "grants": [ { "permission": "plugins.manage", "constraints": { "sites": ["{{docs}}"] } } ] }"""),
            Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        using var manager = host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, "docs-plugins", cancellationToken: Token));

        (await SetStatusAsync(manager, DefaultSite.Id, enabled: false)).ShouldBe(HttpStatusCode.Forbidden);

        using (var disabled = await SetAsync(manager, docs, enabled: false))
        {
            disabled.StatusCode.ShouldBe(HttpStatusCode.OK, await disabled.Content.ReadAsStringAsync(Token));
            var body = JsonNode.Parse(await disabled.Content.ReadAsStringAsync(Token))!;
            (body["siteId"]!.GetValue<Guid>(), body["slug"]!.GetValue<string>(), body["enabled"]!.GetValue<bool>()).ShouldBe((docs, "docs", false));
        }

        (await SetStatusAsync(manager, docs, enabled: false)).ShouldBe(HttpStatusCode.OK);
        (await SetStatusAsync(manager, docs, enabled: true)).ShouldBe(HttpStatusCode.OK);
        (await SetStatusAsync(manager, docs, enabled: true)).ShouldBe(HttpStatusCode.OK);

        var items = await ActivityAsync(host, docs, count: 2);
        items.Select(item => (item.Action, item.TargetId)).ShouldBe([("plugin.enabled", "sample"), ("plugin.disabled", "sample")], ignoreOrder: true);
        (await ActivityAsync(host, DefaultSite.Id, count: 0)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownPluginOrSiteOrAMissingStateIsRefused()
    {
        await using var host = new FluentCmsTestHost();
        using var admin = await AdminAsync(host);

        using var plugin = await SendAsync(admin, HttpMethod.Put, $"/api/management/v1/plugins/missing/sites/{DefaultSite.Id}", DefaultSite.Id, """{ "enabled": false }""");
        using var site = await SetAsync(admin, Guid.CreateVersion7(), enabled: false);
        using var state = await SendAsync(admin, HttpMethod.Put, $"/api/management/v1/plugins/sample/sites/{DefaultSite.Id}", DefaultSite.Id, "{}");

        plugin.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(plugin)).ShouldBe("FCMS1551");
        site.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(site)).ShouldBe("FCMS2301");
        state.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ASitesRowsGoWithTheSite()
    {
        await using var host = new FluentCmsTestHost();
        var docs = await CreateSiteAsync(host);
        await DisableAsync(host, docs);

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(docs, Token);

        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<PluginSiteRow>().CountAsync(Token)).ShouldBe(0);
    }

    private static async Task<Guid> CreateSiteAsync(FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Docs", "docs", "en", Token)).Id;
    }

    private static async Task<HttpClient> AdminAsync(FluentCmsTestHost host) =>
        host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

    // As the host's own code, which ManagementAuthority takes as allowed.
    private static async Task DisableAsync(FluentCmsTestHost host, Guid site) => await SetStateAsync(host, site, enabled: false);

    private static async Task EnableAsync(FluentCmsTestHost host, Guid site) => await SetStateAsync(host, site, enabled: true);

    private static async Task SetStateAsync(FluentCmsTestHost host, Guid site, bool enabled)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<PluginAdministration>().SetEnabledAsync("sample", site, enabled, Token);
    }

    private static async Task PublishAsync(FluentCmsTestHost host, Guid site, string text)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var source = EventSource.FromSystem(host.Time.GetUtcNow()) with { SiteId = site };
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(new SampleNoteAdded(Guid.CreateVersion7(), text) { Source = source }, Token);
    }

    private static async Task<List<Guid?>> OutboxAsync(FluentCmsTestHost host, string handler)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.Handler == handler)
            .Select(message => message.SiteId)
            .ToListAsync(Token);
    }

    private static async Task<OutboxMessage> OutboxRowAsync(FluentCmsTestHost host, string handler)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking().SingleAsync(message => message.Handler == handler, Token);
    }

    private static async Task RunJobsAsync(FluentCmsTestHost host)
    {
        var executor = host.Services.GetRequiredService<JobExecutor>();
        while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
        {
        }
    }

    // The activity rows are written by the outbox, so the outbox is drained until the expected number is there.
    private static async Task<IReadOnlyList<ActivityItem>> ActivityAsync(FluentCmsTestHost host, Guid site, int count)
    {
        var admin = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).In(new SiteId(site));
        IReadOnlyList<ActivityItem> items = [];
        for (var attempt = 0; attempt < (count == 0 ? 1 : 50) && items.Count < count; attempt++)
        {
            await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);
            items = [.. (await admin.RunAsync<IActivityQuery, PagedResult<ActivityItem>>(query => query.QueryAsync(null, null, Token))).Items
                .Where(item => item.TargetType == "plugin")];
            if (items.Count < count)
            {
                await Task.Delay(100, Token);
            }
        }

        return items;
    }

    private static async Task<JsonArray> ListAsync(HttpClient client, Guid site)
    {
        using var response = await SendAsync(client, HttpMethod.Get, "/api/management/v1/plugins", site);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!["items"]!.AsArray();
    }

    private static async Task<HttpStatusCode> SetStatusAsync(HttpClient client, Guid site, bool enabled)
    {
        using var response = await SetAsync(client, site, enabled);
        return response.StatusCode;
    }

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, Guid site, bool enabled) =>
        SendAsync(client, HttpMethod.Put, $"/api/management/v1/plugins/sample/sites/{site}", DefaultSite.Id, $$"""{ "enabled": {{(enabled ? "true" : "false")}} }""");

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path, Guid site)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path, site);
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, Guid site, string? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-Cms-Site", site.ToString());
        if (body is not null)
        {
            request.Content = Json(body);
        }

        return await client.SendAsync(request, Token);
    }

    private static string[] Strings(JsonNode node) => [.. node.AsArray().Select(item => item!.GetValue<string>())];

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))!["code"]!.GetValue<string>();

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}

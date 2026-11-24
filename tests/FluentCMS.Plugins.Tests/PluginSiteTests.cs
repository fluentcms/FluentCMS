using System.Data.Common;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Tests;

// Per-site enablement of #2830, through the sample plugin, disabled in a second site and left on in the default one.
public sealed class PluginSiteTests(PluginSiteFixture fixture) : IClassFixture<PluginSiteFixture>
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADisabledPluginAnswers404OnItsEndpointsInThatSiteFromTheNextRequestAndKeepsServingAnother()
    {
        var host = fixture.Host;
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

    // GET /me/access in a site lists no permission of a plugin whose routes answer 404 there.
    [Fact]
    public async Task TheAccessOfTheCallerLeavesOutThePermissionsOfAPluginDisabledInItsSite()
    {
        var host = fixture.Host;
        var docs = await CreateSiteAsync(host);
        using var admin = await AdminAsync(host);
        await DisableAsync(host, docs);

        using var off = await SendAsync(admin, HttpMethod.Get, "/api/management/v1/me/access", docs);
        using var on = await SendAsync(admin, HttpMethod.Get, "/api/management/v1/me/access", DefaultSite.Id);

        off.StatusCode.ShouldBe(HttpStatusCode.OK);
        Strings(JsonNode.Parse(await off.Content.ReadAsStringAsync(Token))!["permissions"]!).ShouldNotContain(SamplePermissions.NotesManage);
        Strings(JsonNode.Parse(await on.Content.ReadAsStringAsync(Token))!["permissions"]!).ShouldContain(SamplePermissions.NotesManage);
    }

    // The caller is checked before the plugin's state, so an anonymous caller learns nothing of where the plugin is off.
    [Fact]
    public async Task AnAnonymousCallerIsRefusedBeforeTheDisabledPluginAnswers404()
    {
        var host = fixture.Host;
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
        var host = fixture.Host;
        var calls = fixture.Calls;
        var docs = await CreateSiteAsync(host);
        await DisableAsync(host, docs);
        (await host.Services.GetRequiredService<IPluginState>().IsEnabledAsync("sample", new SiteId(docs), Token)).ShouldBeFalse();
        var editor = await host.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
        var hooks = calls.All.Count(call => call == "sample.hook");

        await host.As(editor).In(new SiteId(docs)).Content<Note>().CreateAsync(new Note { Title = "in docs" }, Token);
        calls.All.Count(call => call == "sample.hook").ShouldBe(hooks);
        await host.As(editor).Content<Note>().CreateAsync(new Note { Title = "in default" }, Token);
        calls.All.Count(call => call == "sample.hook").ShouldBe(hooks + 1);

        await PublishAsync(host, docs, "raised in docs");
        await PublishAsync(host, DefaultSite.Id, "raised in default");
        calls.All.ShouldNotContain("sample.event:raised in docs");
        calls.All.ShouldContain("sample.event:raised in default");
        (await OutboxAsync(host, "sample.note-added", "raised in docs", "raised in default")).ShouldBe([DefaultSite.Id]);

        var scheduler = host.Services.GetRequiredService<IJobScheduler>();
        var skipped = await scheduler.EnqueueAsync<SampleNoteJob, SampleNotePayload>(new SampleNotePayload("job in docs"), new JobOptions { Site = new SiteId(docs) }, Token);
        await RunJobsAsync(host);
        var ran = await scheduler.EnqueueAsync<SampleNoteJob, SampleNotePayload>(new SampleNotePayload("job in default"), new JobOptions { Site = new SiteId(DefaultSite.Id) }, Token);
        await RunJobsAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking();
        (await jobs.SingleAsync(job => job.Id == skipped, Token)).Status.ShouldBe(JobStatus.Succeeded);
        (await jobs.SingleAsync(job => job.Id == ran, Token)).Status.ShouldBe(JobStatus.Succeeded);
        (await scope.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>().Set<SampleNote>()
                .Where(note => note.Text == "job in docs" || note.Text == "job in default")
                .Select(note => note.Text)
                .ToListAsync(Token))
            .ShouldBe(["job in default"]);
    }

    // Recorded while the plugin was on, a delivery is the plugin's: it waits without using an attempt, and goes once it is on again.
    [Fact]
    public async Task ADeliveryRecordedBeforeThePluginWasDisabledWaitsAndIsDeliveredOnceItIsEnabledAgain()
    {
        var host = fixture.Host;
        var calls = fixture.Calls;
        var docs = await CreateSiteAsync(host);
        await PublishAsync(host, docs, "before");
        await DisableAsync(host, docs);

        await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token);

        calls.All.ShouldNotContain("sample.outbox:before");
        var waiting = await OutboxRowAsync(host, docs, "sample.note-added");
        (waiting.Status, waiting.Attempts).ShouldBe((OutboxStatus.Pending, 0));
        waiting.AvailableAt.ShouldBeGreaterThan(host.Time.GetUtcNow());

        await EnableAsync(host, docs);
        host.Time.Advance(host.Services.GetRequiredService<OutboxOptions>().MissingHandlerDelay);

        // No worker drains the outbox, so the delivery is made by the time the drain finds nothing due.
        while (await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }

        (await OutboxRowAsync(host, docs, "sample.note-added")).Status.ShouldBe(OutboxStatus.Delivered);
        calls.All.ShouldContain("sample.outbox:before");
    }

    // Owed to the plugin for OrphanAfter only, so that a plugin left off for good leaves a dead letter, not a row claimed for ever.
    [Fact]
    public async Task ADeliveryWhosePluginStaysDisabledPastOrphanAfterIsLeftAsFailedNamingThePlugin()
    {
        var host = fixture.Host;
        var docs = await CreateSiteAsync(host);
        await PublishAsync(host, docs, "before it stays off");
        await DisableAsync(host, docs);
        var drainer = host.Services.GetRequiredService<OutboxDrainer>();
        var options = host.Services.GetRequiredService<OutboxOptions>();

        host.Time.Advance(options.OrphanAfter - TimeSpan.FromMinutes(10));
        await drainer.DrainAsync(Token);
        (await OutboxRowAsync(host, docs, "sample.note-added")).Status.ShouldBe(OutboxStatus.Pending);

        host.Time.Advance(options.MissingHandlerDelay + TimeSpan.FromMinutes(10));
        await drainer.DrainAsync(Token);

        var failed = await OutboxRowAsync(host, docs, "sample.note-added");
        failed.Status.ShouldBe(OutboxStatus.Failed);
        failed.Attempts.ShouldBe(0);
        failed.LastError!.ShouldContain("'sample.note-added'");
        failed.LastError!.ShouldContain("'sample'");
        failed.LastError!.ShouldContain(docs.ToString("D"));
        (await host.Services.GetRequiredService<IEventDeadLetters>().ListAsync(0, 50, Token)).ShouldContain(item => item.Id == failed.Id);
    }

    // The state is read once for a batch, which goes back with one write and no attempt used; the lane waits, and a later drain
    // delivers the batch in the order of its events.
    [Fact]
    public async Task ABatchWhosePluginStateCannotBeReadGoesBackWithOneReadAndOneWrite()
    {
        var fault = new PluginStateFault();
        var writes = new OutboxWrites();
        var calls = new PluginCalls();
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.AddSingleton(calls))
            .Configure(cms =>
            {
                cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(fault, writes));
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[SampleNoteJob.Queue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
        var docs = await CreateSiteAsync(host);
        var drainer = host.Services.GetRequiredService<OutboxDrainer>();
        await drainer.DrainAsync(Token);
        string[] texts = ["first", "second", "third"];
        foreach (var text in texts)
        {
            await PublishAsync(host, docs, text);
        }

        // Armed first, so that no read in between caches the state again. The activity of the notes is delivered in a lane of
        // its own, with one claim and one settle; the sample's lane claims its batch and gives it back.
        fault.Armed = true;
        host.Services.GetRequiredService<PluginState>().Invalidate();
        writes.Reset();
        (await drainer.DrainAsync(Token)).ShouldBe(texts.Length);
        fault.Armed = false;

        fault.Refused.ShouldBe(1);
        writes.Count.ShouldBe(4);
        calls.All.ShouldNotContain(call => call.StartsWith("sample.outbox:", StringComparison.Ordinal));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking().Where(message => message.Handler == "sample.note-added").ToListAsync(Token))
                .ShouldAllBe(message => message.Status == OutboxStatus.Pending && message.Attempts == 0 && message.LeaseOwner == null);
        }

        (await drainer.DrainAsync(Token)).ShouldBe(texts.Length);
        calls.All.Where(call => call.StartsWith("sample.outbox:", StringComparison.Ordinal)).ShouldBe(texts.Select(text => $"sample.outbox:{text}"));
    }

    // The state read for the first delivery is dropped as it is read, so the second reads again, and the host stops during that.
    [Fact]
    public async Task AStopDuringTheReadOfThePluginStateSettlesWhatWasDeliveredAndGivesTheRestBack()
    {
        var stop = new StopAtTheSecondStateRead();
        var calls = new PluginCalls();
        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.AddSingleton(calls))
            .Configure(cms =>
            {
                cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(stop));
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[SampleNoteJob.Queue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
        var docs = await CreateSiteAsync(host);
        var drainer = host.Services.GetRequiredService<OutboxDrainer>();
        await drainer.DrainAsync(Token);
        string[] texts = ["first", "second", "third"];
        foreach (var text in texts)
        {
            await PublishAsync(host, docs, text);
        }

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var state = host.Services.GetRequiredService<PluginState>();
        stop.Arm(state, stopping);
        state.Invalidate();
        await drainer.DrainAsync(stopping.Token);

        calls.All.Where(call => call.StartsWith("sample.outbox:", StringComparison.Ordinal)).ShouldBe(["sample.outbox:first"]);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking()
                    .Where(message => message.Handler == "sample.note-added")
                    .OrderBy(message => message.Sequence)
                    .Select(message => new { message.Status, message.Attempts, Leased = message.LeaseOwner != null })
                    .ToListAsync(Token))
                .Select(row => (row.Status, row.Attempts, row.Leased))
                .ShouldBe([(OutboxStatus.Delivered, 1, false), (OutboxStatus.Pending, 0, false), (OutboxStatus.Pending, 0, false)]);
        }

        stop.Disarm();
        await drainer.DrainAsync(Token);
        calls.All.Where(call => call.StartsWith("sample.outbox:", StringComparison.Ordinal)).ShouldBe(texts.Select(text => $"sample.outbox:{text}"));
    }

    // As the drain does with a delivery: the job did not run, so it uses no attempt, and it waits one poll before it is tried again.
    [Fact]
    public async Task AJobWhosePluginStateCannotBeReadWaitsWithoutUsingAnAttempt()
    {
        var fault = new PluginStateFault();
        await using var host = new FluentCmsTestHost().Configure(cms =>
        {
            cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(fault));
            cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
            cms.Jobs.Options.Queues[SampleNoteJob.Queue] = 0;
            cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
        });
        var docs = await CreateSiteAsync(host);
        var id = await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<SampleNoteJob, SampleNotePayload>(new SampleNotePayload("deferred"), new JobOptions { Site = new SiteId(docs) }, Token);
        var before = await JobAsync(host, id);
        var executor = host.Services.GetRequiredService<JobExecutor>();

        // Armed first, so that no read in between caches the state again.
        fault.Armed = true;
        host.Services.GetRequiredService<PluginState>().Invalidate();
        (await executor.RunNextAsync(before.Queue, "test", Token)).ShouldBeTrue();
        fault.Armed = false;

        var released = await JobAsync(host, id);
        released.Status.ShouldBe(JobStatus.Pending);
        (released.MaxAttempts - released.Attempts).ShouldBe(before.MaxAttempts - before.Attempts);
        released.RunAt.ShouldBe(host.Time.GetUtcNow() + host.Services.GetRequiredService<JobRunnerOptions>().PollInterval);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<JobAttempt>().AsNoTracking().SingleAsync(attempt => attempt.JobId == id, Token))
                .Outcome.ShouldBe(JobAttemptOutcome.Released);
        }

        (await executor.RunNextAsync(before.Queue, "test", Token)).ShouldBeFalse();
        host.Time.Advance(host.Services.GetRequiredService<JobRunnerOptions>().PollInterval);
        (await executor.RunNextAsync(before.Queue, "test", Token)).ShouldBeTrue();

        (await JobAsync(host, id)).Status.ShouldBe(JobStatus.Succeeded);
        await using var read = host.Services.CreateAsyncScope();
        (await read.ServiceProvider.GetRequiredService<PluginDbContext<SamplePlugin>>().Set<SampleNote>().Select(note => note.Text).ToListAsync(Token)).ShouldBe(["deferred"]);
    }

    [Fact]
    public async Task ThePluginListHasEveryPluginWithItsManifestContributionsAndEnablementPerSite()
    {
        var host = fixture.Host;
        var slug = NewSlug();
        var docs = await CreateSiteAsync(host, slug);
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
        Strings(contributions["tools"]!).ShouldBe([SampleTools.WhoAmI, SampleTools.EntryCreate, SampleTools.EntryTitles, SampleTools.NoteAdd]);
        contributions["endpoints"]!.AsArray().Select(endpoint => $"{endpoint!["method"]!.GetValue<string>()} {endpoint["route"]!.GetValue<string>()}")
            .ShouldBe(["GET /api/delivery/v1/sample/fresh", "GET /api/delivery/v1/sample/greeting", "GET /api/management/v1/sample/notes", "POST /api/management/v1/sample/notes", "GET /sample/banner", "GET /sample/greeting"]);
        // The sites of the other tests are listed too.
        sample["sites"]!.AsArray().Select(site => (Id: site!["siteId"]!.GetValue<Guid>(), Slug: site["slug"]!.GetValue<string>(), Enabled: site["enabled"]!.GetValue<bool>()))
            .Where(site => site.Id == DefaultSite.Id || site.Id == docs)
            .ShouldBe([(DefaultSite.Id, DefaultSite.Slug, true), (docs, slug, false)]);

        var inDocs = (await ListAsync(admin, docs)).Single(item => item!["name"]!.GetValue<string>() == "sample")!;
        inDocs["enabled"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task ACallerWithoutPluginsManageReadsTheListForItsSiteOnly()
    {
        var host = fixture.Host;
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
        var host = fixture.Host;
        var slug = NewSlug();
        var docs = await CreateSiteAsync(host, slug);
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
            (body["siteId"]!.GetValue<Guid>(), body["slug"]!.GetValue<string>(), body["enabled"]!.GetValue<bool>()).ShouldBe((docs, slug, false));
        }

        (await SetStatusAsync(manager, docs, enabled: false)).ShouldBe(HttpStatusCode.OK);
        (await SetStatusAsync(manager, docs, enabled: true)).ShouldBe(HttpStatusCode.OK);
        (await SetStatusAsync(manager, docs, enabled: true)).ShouldBe(HttpStatusCode.OK);

        var items = await ActivityAsync(host, docs);
        items.Select(item => (item.Action, item.TargetId)).ShouldBe([("plugin.enabled", "sample"), ("plugin.disabled", "sample")], ignoreOrder: true);
        (await ActivityAsync(host, DefaultSite.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownPluginOrSiteOrAMissingStateIsRefused()
    {
        var host = fixture.Host;
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
        var host = fixture.Host;
        var docs = await CreateSiteAsync(host);
        await DisableAsync(host, docs);

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().DeleteAsync(docs, Token);

        (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<PluginSiteRow>().CountAsync(row => row.SiteId == docs, Token)).ShouldBe(0);
    }

    // #3306: the readers that find the state of the plugins expired at once share one read of cms_plugin_site.
    [Fact]
    public async Task ReadersThatFindThePluginStateExpiredAtOnceReadItOnce()
    {
        var reads = new HeldReads("cms_plugin_site");
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .WithServices(services => services.AddSingleton(new PluginCalls()))
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
                cms.Services.ConfigureDbContext<SystemDbContext>(options => options.AddInterceptors(reads));
            });
        var state = host.Services.GetRequiredService<IPluginState>();
        var site = new SiteId(Guid.CreateVersion7());
        (await state.IsEnabledAsync("sample", site, Token)).ShouldBeTrue();

        host.Time.Advance(PluginState.CacheDuration);
        reads.Hold();

        // A call returns once it has joined the read under way or begun its own, so all twenty have found the state expired
        // before the read is let go.
        var readers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Factory.StartNew(
            () => state.IsEnabledAsync("sample", site, Token).AsTask(),
            Token,
            TaskCreationOptions.None,
            TaskScheduler.Default))).WaitAsync(TimeSpan.FromSeconds(30), Token);
        reads.Release();

        (await Task.WhenAll(readers)).ShouldAllBe(enabled => enabled);
        reads.Count.ShouldBe(1);
    }

    // A site of the test's own, since the tests share a host.
    private static async Task<Guid> CreateSiteAsync(FluentCmsTestHost host, string? slug = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SiteManager>().CreateAsync("Docs", slug ?? NewSlug(), "en", Token)).Id;
    }

    private static string NewSlug() => $"docs-{Guid.NewGuid():N}";

    private static async Task<HttpClient> AdminAsync(FluentCmsTestHost host) =>
        host.AsKey(await host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

    // As the host's own code, which ManagementAuthority takes as allowed.
    private static async Task DisableAsync(FluentCmsTestHost host, Guid site) => await SetStateAsync(host, site, enabled: false);

    private static async Task EnableAsync(FluentCmsTestHost host, Guid site) => await SetStateAsync(host, site, enabled: true);

    private static async Task SetStateAsync(FluentCmsTestHost host, Guid site, bool enabled)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), Origin.Seed, new SiteId(DefaultSite.Id));
        await scope.ServiceProvider.GetRequiredService<PluginAdministration>().SetEnabledAsync("sample", site, enabled, Token);
    }

    private static async Task PublishAsync(FluentCmsTestHost host, Guid site, string text)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var source = EventSource.FromSystem(host.Time.GetUtcNow()) with { SiteId = site };
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(new SampleNoteAdded(Guid.CreateVersion7(), text) { Source = source }, Token);
    }

    // The sites of the handler's rows for the events that carried one of the texts.
    private static async Task<List<Guid?>> OutboxAsync(FluentCmsTestHost host, string handler, params string[] texts)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.Handler == handler)
            .ToListAsync(Token);
        return [.. rows.Where(message => texts.Any(text => message.Payload.Contains(text, StringComparison.Ordinal))).Select(message => message.SiteId)];
    }

    private static async Task<OutboxMessage> OutboxRowAsync(FluentCmsTestHost host, Guid site, string handler)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking()
            .SingleAsync(message => message.Handler == handler && message.SiteId == site, Token);
    }

    private static async Task<Job> JobAsync(FluentCmsTestHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking().SingleAsync(job => job.Id == id, Token);
    }

    // Fails the read of which plugins are disabled per site while it is armed.
    private sealed class PluginStateFault : DbCommandInterceptor
    {
        public volatile bool Armed;

        private int _refused;

        public int Refused => Volatile.Read(ref _refused);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("cms_plugin_site", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _refused);
                throw new InvalidOperationException("The database went away.");
            }

            return ValueTask.FromResult(result);
        }
    }

    // Once armed, makes the first read of the state stale and cancels the second as a stopping host does.
    private sealed class StopAtTheSecondStateRead : DbCommandInterceptor
    {
        private PluginState? _state;
        private CancellationTokenSource? _stopping;
        private int _reads;

        public void Arm(PluginState state, CancellationTokenSource stopping) => (_state, _stopping) = (state, stopping);

        public void Disarm() => (_state, _stopping) = (null, null);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (_stopping is { } stopping && command.CommandText.Contains("cms_plugin_site", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _reads) == 1)
                {
                    _state!.Invalidate();
                }
                else
                {
                    await stopping.CancelAsync();
                    stopping.Token.ThrowIfCancellationRequested();
                }
            }

            return result;
        }
    }

    // Counts the updates of cms_outbox.
    private sealed class OutboxWrites : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("cms_outbox", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }

            return ValueTask.FromResult(result);
        }
    }

    private static async Task RunJobsAsync(FluentCmsTestHost host)
    {
        var executor = host.Services.GetRequiredService<JobExecutor>();
        while (await executor.RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token))
        {
        }
    }

    // The activity rows are written by the outbox, which no worker drains, so they are there once it is drained.
    private static async Task<IReadOnlyList<ActivityItem>> ActivityAsync(FluentCmsTestHost host, Guid site)
    {
        var admin = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).In(new SiteId(site));
        while (await host.Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }

        return (await admin.RunAsync<IActivityQuery, PagedResult<ActivityItem>>(query => query.QueryAsync(new ActivityFilter { TargetType = "plugin" }, null, Token))).Items;
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

    // Counts the reads of one table once held, also those that come after the release, and keeps each waiting until
    // released, so that whatever reads it while one read is under way is seen.
    private sealed class HeldReads(string table) : DbCommandInterceptor
    {
        private TaskCompletionSource? _release;
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Hold()
        {
            _count = 0;
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Release() => Volatile.Read(ref _release)?.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _release) is { } release && command.CommandText.Contains(table, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
                await release.Task;
            }

            return result;
        }
    }
}

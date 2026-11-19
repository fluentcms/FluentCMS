using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

// Attempts left queued with no job, as when the job that should have woken them was dead-lettered.
public sealed class WebhookRecoveryTests(WebhookFixture hosts) : IClassFixture<WebhookFixture>
{
    [Fact]
    public async Task AQueueWhoseWakeUpWasLostIsSentInOrderByTheQueueJobAndKeptByRetention()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var webhook = Id(await CreateAsync(client, new { name = "stalled", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));
        var now = host.Time.GetUtcNow();
        var first = Queued(webhook, now - TimeSpan.FromDays(40), """{"n":1}""");
        var second = Queued(webhook, now - TimeSpan.FromDays(39), """{"n":2}""");
        await AddAsync(host, second, first);

        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<WebhookRetentionJob, WebhookRetentionPayload>(new WebhookRetentionPayload(), cancellationToken: Token);
        (await RunDueJobsAsync(host)).ShouldBe(1);
        (await DeliveriesAsync(host, webhook)).Count.ShouldBe(2, "retention keeps an attempt that waits to be sent");
        receiver.Requests.ShouldBeEmpty("nothing wakes the queue on its own");

        await WakeAsync(host);
        (await RunDueJobsAsync(host)).ShouldBe(3, "the queue job, then each attempt's job");
        receiver.Requests.Select(request => request.Body).ShouldBe([first.RequestBody, second.RequestBody]);
        (await DeliveriesAsync(host, webhook)).ShouldAllBe(delivery => delivery.State == DeliveryStates.Succeeded);
    }

    [Fact]
    public async Task TheQueueOfAWebhookDisabledWhileNothingWasInFlightIsDropped()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var webhook = Id(await CreateAsync(client, new { name = "off", url = receiver.Url().ToString(), events = new List<string> { "content.published" }, enabled = false }));
        var now = host.Time.GetUtcNow();
        var retry = Queued(webhook, now - TimeSpan.FromMinutes(20), """{"n":1}""", attempt: 2);
        var failed = new WebhookDelivery
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = retry.DeliveryId,
            WebhookId = webhook,
            EventId = retry.EventId,
            Event = retry.Event,
            Attempt = 1,
            State = DeliveryStates.Failed,
            RequestBody = retry.RequestBody,
            NextRetryAt = now - TimeSpan.FromMinutes(20),
            CreatedAt = now - TimeSpan.FromMinutes(21),
        };
        await AddAsync(host, failed, retry);

        await WakeAsync(host);
        await RunDueJobsAsync(host);

        receiver.Requests.ShouldBeEmpty();
        var left = (await DeliveriesAsync(host, webhook)).ShouldHaveSingleItem();
        (left.Attempt, left.State, left.NextRetryAt).ShouldBe((1, DeliveryStates.Dead, (DateTimeOffset?)null));
    }

    // The gap between an attempt that ended and its head's job claiming the next is not a stall, however busy the
    // webhooks queue is.
    [Fact]
    public async Task AQueueWithARecentClaimIsLeftAloneUntilItHasStalled()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var webhook = Id(await CreateAsync(client, new { name = "busy", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));
        var now = host.Time.GetUtcNow();
        var ended = Queued(webhook, now - TimeSpan.FromMinutes(1), """{"n":0}""");
        ended.State = DeliveryStates.Succeeded;
        var head = Queued(webhook, now - TimeSpan.FromMinutes(20), """{"n":1}""");
        await AddAsync(host, ended, head);

        await WakeAsync(host);
        (await RunDueJobsAsync(host)).ShouldBe(1, "the last claim is a minute old");
        receiver.Requests.ShouldBeEmpty();

        host.Time.Advance(WebhookQueueJob.StalledAfter);
        await WakeAsync(host);
        (await RunDueJobsAsync(host)).ShouldBe(2);
        receiver.Requests.ShouldHaveSingleItem().Body.ShouldBe(head.RequestBody);
    }

    // A queued head with an old event, such as a late retry, behind a recent claim is a queue that drains, so a delivery
    // queued behind it wakes nobody; once the last claim is old too, the queue has stalled and the next delivery wakes it.
    [Fact]
    public async Task ADeliveryQueuedBehindAHeadWakesItOnlyOnceTheQueueHasStalled()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var webhook = Id(await CreateAsync(client, new { name = "late retry", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));
        var now = host.Time.GetUtcNow();
        var ended = Queued(webhook, now - TimeSpan.FromMinutes(1), """{"n":0}""");
        ended.State = DeliveryStates.Succeeded;
        var head = Queued(webhook, now - TimeSpan.FromMinutes(30), """{"n":1}""", attempt: 3);
        await AddAsync(host, ended, head);
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();

        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Behind" }, Token)).Id, cancellationToken: Token);
        (await PumpAsync(host)).ShouldBe(1, "the delivery queues behind the head and enqueues nothing");
        receiver.Requests.ShouldBeEmpty();

        host.Time.Advance(WebhookQueueJob.StalledAfter);
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Wakes it" }, Token)).Id, cancellationToken: Token);
        (await PumpAsync(host)).ShouldBe(4, "the new delivery, then the head's job and each delivery's");
        receiver.Requests.Count.ShouldBe(3);
        receiver.Requests[0].Body.ShouldBe(head.RequestBody);
    }

    [Fact]
    public async Task RetentionRemovesAnOldQueuedAttemptWhoseWebhookIsGone()
    {
        var host = await hosts.HostAsync();
        var now = host.Time.GetUtcNow();
        var orphan = Queued(Guid.CreateVersion7(), now - TimeSpan.FromDays(31), "{}");
        await AddAsync(host, orphan);

        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<WebhookRetentionJob, WebhookRetentionPayload>(new WebhookRetentionPayload(), cancellationToken: Token);
        (await RunDueJobsAsync(host)).ShouldBe(1);

        (await DeliveriesAsync(host, orphan.WebhookId)).ShouldBeEmpty();
    }

    // Without statistics, SQLite picks an index only when it holds the whole order of the query; the plain index of the
    // webhook's log would make it read the log and sort.
    [Fact]
    public async Task OnSqliteTheHeadQueryReadsThePartialIndexAndSortsNothing()
    {
        var host = await hosts.HostAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
        database.Database.ProviderName.ShouldBe("Microsoft.EntityFrameworkCore.Sqlite");
        await using var command = WebhookDeliveryJob.Head(database.Set<WebhookDelivery>().AsNoTracking(), Guid.CreateVersion7()).Take(1).CreateDbCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        await database.Database.OpenConnectionAsync(Token);
        List<string> plan = [];
        await using (var reader = await command.ExecuteReaderAsync(Token))
        {
            while (await reader.ReadAsync(Token))
            {
                plan.Add(reader.GetString(reader.GetOrdinal("detail")));
            }
        }

        plan.ShouldHaveSingleItem().ShouldContain("USING INDEX ix_cms_webhooks_delivery_queued");
    }

    [Fact]
    public async Task TheQueueJobRunsEveryMinuteOnceTheHostHasStarted()
    {
        await using var host = Host();

        await StartedAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var schedule = await scope.ServiceProvider.GetRequiredService<Core.Storage.SystemDbContext>().Set<RecurringJob>()
            .AsNoTracking()
            .FirstOrDefaultAsync(job => job.Name == WebhookQueueJob.Type, Token);

        schedule.ShouldNotBeNull();
        (schedule.Type, schedule.Cron).ShouldBe((WebhookQueueJob.Type, WebhookQueueJob.Schedule));
    }

    private static Task<Guid> WakeAsync(FluentCmsTestHost host) =>
        host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<WebhookQueueJob, WebhookQueuePayload>(new WebhookQueuePayload(), cancellationToken: Token);

    private static WebhookDelivery Queued(Guid webhook, DateTimeOffset at, string body, int attempt = 1) => new()
    {
        Id = Guid.CreateVersion7(),
        DeliveryId = Guid.CreateVersion7(),
        WebhookId = webhook,
        EventId = Guid.CreateVersion7(),
        Event = "content.published",
        Attempt = attempt,
        State = DeliveryStates.Queued,
        RequestBody = body,
        CreatedAt = at,
    };

    private static async Task AddAsync(FluentCmsTestHost host, params WebhookDelivery[] rows)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
        database.AddRange(rows);
        await database.SaveChangesAsync(Token);
    }
}

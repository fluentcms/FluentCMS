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
        var retry = Queued(webhook, now, """{"n":1}""", attempt: 2);
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
            NextRetryAt = now,
            CreatedAt = now - TimeSpan.FromMinutes(1),
        };
        await AddAsync(host, failed, retry);

        await WakeAsync(host);
        await RunDueJobsAsync(host);

        receiver.Requests.ShouldBeEmpty();
        var left = (await DeliveriesAsync(host, webhook)).ShouldHaveSingleItem();
        (left.Attempt, left.State, left.NextRetryAt).ShouldBe((1, DeliveryStates.Dead, (DateTimeOffset?)null));
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

using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed class WebhookRetentionTests(WebhookFixture hosts) : IClassFixture<WebhookFixture>
{
    [Fact]
    public async Task TheRetentionJobRemovesTheAttemptsOlderThanThirtyDays()
    {
        var host = await hosts.HostAsync();
        var now = StorageConventions.ToMicroseconds(host.Time.GetUtcNow());
        var webhook = Guid.CreateVersion7();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
            database.AddRange(Attempt(webhook, now - TimeSpan.FromDays(31)), Attempt(webhook, now - TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1)), Attempt(webhook, now - TimeSpan.FromDays(29)), Attempt(webhook, now));
            await database.SaveChangesAsync(Token);
        }

        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<WebhookRetentionJob, WebhookRetentionPayload>(new WebhookRetentionPayload(), cancellationToken: Token);
        (await RunDueJobsAsync(host)).ShouldBe(1);

        (await DeliveriesAsync(host, webhook)).Select(delivery => delivery.CreatedAt).ShouldBe([now - TimeSpan.FromDays(29), now]);
    }

    [Fact]
    public async Task TheRetentionJobHasAnAttemptTimeout()
    {
        var host = await hosts.HostAsync();

        host.Services.GetRequiredService<JobRegistry>().Find(WebhookRetentionJob.Type)!.AttemptTimeout.ShouldBe(WebhookRetentionJob.AttemptTimeout);
    }

    [Fact]
    public async Task TheRetentionRunsDailyOnceTheHostHasStarted()
    {
        // What it reads is what the start of a host stores.
        await using var host = Host();

        await StartedAsync(host);
        await using var scope = host.Services.CreateAsyncScope();
        var schedule = await scope.ServiceProvider.GetRequiredService<Core.Storage.SystemDbContext>().Set<RecurringJob>()
            .AsNoTracking()
            .FirstOrDefaultAsync(job => job.Name == WebhookRetentionJob.Type, Token);

        schedule.ShouldNotBeNull();
        (schedule.Type, schedule.Cron).ShouldBe((WebhookRetentionJob.Type, WebhookRetentionJob.Schedule));
    }

    private static WebhookDelivery Attempt(Guid webhook, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        DeliveryId = Guid.CreateVersion7(),
        WebhookId = webhook,
        EventId = Guid.CreateVersion7(),
        Event = "content.published",
        Attempt = 1,
        State = DeliveryStates.Succeeded,
        StatusCode = 200,
        RequestBody = "{}",
        CreatedAt = at,
    };
}

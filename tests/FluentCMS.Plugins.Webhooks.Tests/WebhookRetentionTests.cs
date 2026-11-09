using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Storage;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed class WebhookRetentionTests
{
    [Fact]
    public async Task TheRetentionJobRemovesTheAttemptsOlderThanThirtyDays()
    {
        await using var host = Host();
        var now = StorageConventions.ToMicroseconds(host.Time.GetUtcNow());
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
            database.AddRange(Attempt(now - TimeSpan.FromDays(31)), Attempt(now - TimeSpan.FromDays(30) - TimeSpan.FromSeconds(1)), Attempt(now - TimeSpan.FromDays(29)), Attempt(now));
            await database.SaveChangesAsync(Token);
        }

        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<WebhookRetentionJob, WebhookRetentionPayload>(new WebhookRetentionPayload(), cancellationToken: Token);
        (await RunDueJobsAsync(host)).ShouldBe(1);

        (await DeliveriesAsync(host)).Select(delivery => delivery.CreatedAt).ShouldBe([now - TimeSpan.FromDays(29), now]);
    }

    [Fact]
    public async Task TheRetentionJobHasAnAttemptTimeout()
    {
        await using var host = Host();

        host.Services.GetRequiredService<JobRegistry>().Find(WebhookRetentionJob.Type)!.AttemptTimeout.ShouldBe(WebhookRetentionJob.AttemptTimeout);
    }

    [Fact]
    public async Task TheRetentionRunsDailyOnceTheHostHasStarted()
    {
        await using var host = Host();

        RecurringJob? schedule = null;
        for (var attempt = 0; attempt < 400 && schedule is null; attempt++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            schedule = await scope.ServiceProvider.GetRequiredService<Core.Storage.SystemDbContext>().Set<RecurringJob>()
                .AsNoTracking()
                .FirstOrDefaultAsync(job => job.Name == WebhookRetentionJob.Type, Token);
            if (schedule is null)
            {
                await Task.Delay(50, Token);
            }
        }

        schedule.ShouldNotBeNull();
        (schedule.Type, schedule.Cron).ShouldBe((WebhookRetentionJob.Type, WebhookRetentionJob.Schedule));
    }

    private static WebhookDelivery Attempt(DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        DeliveryId = Guid.CreateVersion7(),
        WebhookId = Guid.CreateVersion7(),
        EventId = Guid.CreateVersion7(),
        Event = "content.published",
        Attempt = 1,
        State = DeliveryStates.Succeeded,
        StatusCode = 200,
        RequestBody = "{}",
        CreatedAt = at,
    };
}

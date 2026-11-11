using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.AiAssist.Tests.AiTesting;

namespace FluentCMS.Plugins.AiAssist.Tests;

public sealed class AiRetentionTests(AiHost ai) : IClassFixture<AiHost>
{
    [Fact]
    public async Task TheRetentionJobRemovesTheRowsOlderThanNinetyDays()
    {
        var site = Guid.CreateVersion7();
        var now = ai.Host.Time.GetUtcNow();
        await using (var scope = ai.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<AiAssistPlugin>>();
            database.AddRange(Row(site, now - TimeSpan.FromDays(91)), Row(site, now - TimeSpan.FromDays(90) - TimeSpan.FromSeconds(1)), Row(site, now - TimeSpan.FromDays(89)), Row(site, now));
            await database.SaveChangesAsync(Token);
        }

        await ai.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<AiUsageRetentionJob, AiUsageRetentionPayload>(new AiUsageRetentionPayload(), cancellationToken: Token);
        await using (var scope = ai.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token)).ShouldBeTrue();
        }

        (await ai.UsageAsync(site)).Select(usage => usage.CreatedAt).ShouldBe([now - TimeSpan.FromDays(89), now]);
    }

    [Fact]
    public async Task TheRetentionRunsDailyOnceTheHostHasStartedAndHasAnAttemptTimeout()
    {
        await ai.Services.GetServices<IHostedService>().OfType<AiUsageRetentionSchedule>().Single().ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await using var scope = ai.Services.CreateAsyncScope();
        var schedule = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>()
            .AsNoTracking()
            .FirstOrDefaultAsync(job => job.Name == AiUsageRetentionJob.Type, Token);

        schedule.ShouldNotBeNull();
        (schedule.Type, schedule.Cron).ShouldBe((AiUsageRetentionJob.Type, AiUsageRetentionJob.Schedule));
        ai.Services.GetRequiredService<JobRegistry>().Find(AiUsageRetentionJob.Type)!.AttemptTimeout.ShouldBe(AiUsageRetentionJob.AttemptTimeout);
    }

    private static AiUsage Row(Guid site, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        SiteId = site,
        PrincipalKind = "apiKey",
        PrincipalId = Guid.CreateVersion7(),
        Action = "rewrite",
        Type = "article",
        Field = "summary",
        Provider = "scripted",
        Model = "test-model",
        Succeeded = true,
        CreatedAt = at,
    };
}

using System.Net;
using System.Text;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

// Per-site enablement (#2830) gates the plugin through core alone: its outbox handlers, its delivery jobs and its endpoints.
public sealed class WebhookSiteTests
{
    [Fact]
    public async Task WebhooksDisabledForASiteSendNothingThereAndAnswer404UntilEnabledAgain()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var host = Host();
        using var client = await AdminAsync(host);
        var webhook = await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" } });
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();

        (await SetAsync(client, enabled: false)).ShouldBe(HttpStatusCode.OK);
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "While off" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);

        receiver.Requests.ShouldBeEmpty();
        (await OutboxHandlersAsync(host)).ShouldNotContain(handler => handler.StartsWith("webhooks.", StringComparison.Ordinal));
        using (var list = await client.GetAsync(new Uri("/api/management/v1/webhooks", UriKind.Relative), Token))
        {
            list.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await CodeAsync(list)).ShouldBe("FCMS1550");
        }

        // A manual attempt is sent even to a disabled webhook, so only the plugin's gate keeps this one from going.
        var attempt = new WebhookAttempt(Id(webhook), Guid.CreateVersion7(), Guid.CreateVersion7(), "content.published", 1, Manual: true, "{}");
        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<WebhookDeliveryJob, WebhookAttempt>(attempt, new JobOptions { Site = new SiteId(DefaultSite.Id) }, Token);
        await host.Services.GetRequiredService<IJobScheduler>().EnqueueAsync<WebhookRetentionJob, WebhookRetentionPayload>(new WebhookRetentionPayload(), cancellationToken: Token);
        (await RunDueJobsAsync(host)).ShouldBe(2);
        receiver.Requests.ShouldBeEmpty();
        (await DeliveriesAsync(host)).ShouldBeEmpty();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<Job>().AsNoTracking().Where(job => job.Type == WebhookRetentionJob.Type).Select(job => job.Status).ToListAsync(Token))
                .ShouldBe([JobStatus.Succeeded]);
        }

        (await SetAsync(client, enabled: true)).ShouldBe(HttpStatusCode.OK);
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Back on" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);

        receiver.Requests.ShouldHaveSingleItem();
    }

    private static async Task<HttpStatusCode> SetAsync(HttpClient client, bool enabled)
    {
        using var content = new StringContent($$"""{ "enabled": {{(enabled ? "true" : "false")}} }""", Encoding.UTF8, "application/json");
        using var response = await client.PutAsync(new Uri($"/api/management/v1/plugins/webhooks/sites/{DefaultSite.Id}", UriKind.Relative), content, Token);
        return response.StatusCode;
    }

    private static async Task<List<string>> OutboxHandlersAsync(Testing.FluentCmsTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<OutboxMessage>().AsNoTracking().Select(message => message.Handler).ToListAsync(Token);
    }
}

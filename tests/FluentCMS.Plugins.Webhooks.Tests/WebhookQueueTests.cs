using System.Collections.Concurrent;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Jobs;
using FluentCMS.Events;
using FluentCMS.Publishing;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed class WebhookQueueTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task TheWebhooksQueueTakesItsWorkersFromTheOptionsUnlessTheHostSetsThem()
    {
        await using (var defaults = new FluentCmsTestHost())
        {
            defaults.Services.GetRequiredService<JobRunnerOptions>().Queues[WebhookDeliveryJob.Queue].ShouldBe(2);
        }

        await using (var configured = new FluentCmsTestHost().WithServices(services => services.PostConfigure<WebhooksOptions>(options => options.Workers = 5)))
        {
            configured.Services.GetRequiredService<JobRunnerOptions>().Queues[WebhookDeliveryJob.Queue].ShouldBe(5);
        }

        await using var host = new FluentCmsTestHost()
            .WithServices(services => services.PostConfigure<WebhooksOptions>(options => options.Workers = 5))
            .Configure(cms => cms.Jobs.Options.Queues[WebhookDeliveryJob.Queue] = 1);
        host.Services.GetRequiredService<JobRunnerOptions>().Queues[WebhookDeliveryJob.Queue].ShouldBe(1);
    }

    [Fact]
    public async Task EveryDeliveryRetryRedeliveryAndPingRunsOnTheWebhooksQueue()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Status = 500;
        await using var host = Host();
        using var client = await AdminAsync(host);
        var webhook = await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" } });
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Queued" }, Token)).Id, cancellationToken: Token);
        await DrainOutboxAsync(host);

        (await RunNextAsync(host, JobRunnerOptions.DefaultQueue)).ShouldBeFalse("a delivery is not on the default queue");
        (await RunNextAsync(host, WebhookDeliveryJob.Queue)).ShouldBeTrue();
        host.Time.Advance(TimeSpan.FromMinutes(1));
        (await RunNextAsync(host, JobRunnerOptions.DefaultQueue)).ShouldBeFalse("nor is its retry");
        (await RunNextAsync(host, WebhookDeliveryJob.Queue)).ShouldBeTrue();

        var root = $"/api/management/v1/webhooks/{Id(webhook):D}";
        var first = (await DeliveriesAsync(host))[0];
        using (await client.PostAsync(new Uri($"{root}/deliveries/{first.DeliveryId:D}/redeliver", UriKind.Relative), null, Token))
        using (await client.PostAsync(new Uri($"{root}/ping", UriKind.Relative), null, Token))
        {
        }

        (await RunNextAsync(host, JobRunnerOptions.DefaultQueue)).ShouldBeFalse("nor a redelivery or a ping");
        (await RunNextAsync(host, WebhookDeliveryJob.Queue)).ShouldBeTrue();
        (await RunNextAsync(host, WebhookDeliveryJob.Queue)).ShouldBeTrue();
        receiver.Requests.Count.ShouldBe(4);
    }

    // Deliveries used to share the default queue, whose two workers a receiver that hangs held for the whole timeout. Three
    // webhooks hold every worker of their queue, which one webhook would not, since it has one attempt in flight at a time.
    [Fact]
    public async Task AHangingReceiverDoesNotDelayAScheduledPublishOnTheSameNode()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Hold();
        var published = new Published();
        await using var host = new FluentCmsTestHost()
            .WithContentTypes<Article, Page>()
            .WithServices(services =>
            {
                services.PostConfigure<WebhooksOptions>(options =>
                {
                    options.AllowPrivateNetworks = true;
                    options.Timeout = TimeSpan.FromMinutes(5);
                    options.Workers = 2;
                });
                services.AddSingleton(published);
            })
            .Configure(cms =>
            {
                cms.Jobs.Options.PollInterval = TimeSpan.FromMilliseconds(50);
                cms.Events.AddHandler<EntryPublishedEvent, Published.Handler>();
            });
        using var client = await AdminAsync(host);
        for (var index = 0; index < 3; index++)
        {
            await CreateAsync(client, new { name = $"dark {index}", url = receiver.Url($"/hook/{index}").ToString(), events = new List<string> { "content.published" } });
        }

        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Burst" }, Token)).Id, cancellationToken: Token);
        await receiver.ArrivedAsync(2).WaitAsync(Patience, Token);

        var scheduled = await articles.CreateAsync(new Article { Title = "Scheduled" }, Token);
        await articles.ScheduleAsync(scheduled.Id, new ScheduleRequest(ScheduleAction.Publish, host.Time.GetUtcNow().UtcDateTime.AddSeconds(30)), cancellationToken: Token);
        host.Time.Advance(TimeSpan.FromSeconds(30));

        await published.Of(scheduled.Id).WaitAsync(Patience, Token);
        receiver.Requests.Count.ShouldBe(2, "the receiver holds both workers of the webhooks queue, and the third delivery waits for one");
        receiver.Release();
    }

    [Fact]
    public async Task TwoDeliveriesToOneWebhookNeverRunAtOnce()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Hold();
        await using var host = Host();
        using var client = await AdminAsync(host);
        await CreateAsync(client, new { name = "slow", url = receiver.Url().ToString(), events = new List<string> { "content.published" } });
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "One" }, Token)).Id, cancellationToken: Token);
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Two" }, Token)).Id, cancellationToken: Token);
        await DrainOutboxAsync(host);

        Task<bool>[] workers = [RunNextAsync(host, WebhookDeliveryJob.Queue), RunNextAsync(host, WebhookDeliveryJob.Queue)];
        var waited = await (await Task.WhenAny(workers)).WaitAsync(Patience, Token);
        waited.ShouldBeTrue();
        await receiver.Entered.WaitAsync(Patience, Token);
        receiver.Requests.Count.ShouldBe(1);
        var pending = (await DeliveriesAsync(host)).ShouldHaveSingleItem();
        pending.State.ShouldBe(DeliveryStates.Pending);

        receiver.Release();
        (await Task.WhenAll(workers).WaitAsync(Patience, Token)).ShouldAllBe(ran => ran);
        (await RunDueJobsAsync(host)).ShouldBe(0, "the second delivery waits a few seconds");
        host.Time.Advance(WebhookDeliveryJob.Busy * 2);
        (await RunDueJobsAsync(host)).ShouldBe(1);

        receiver.Requests.Count.ShouldBe(2);
        receiver.MostOpen.ShouldBe(1);
        var deliveries = await DeliveriesAsync(host);
        deliveries.Count.ShouldBe(2);
        deliveries.ShouldAllBe(delivery => delivery.State == DeliveryStates.Succeeded);
        deliveries.Select(delivery => delivery.DeliveryId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task AnotherWebhookIsNotHeldUpByABusyOne()
    {
        await using var slow = await TestReceiver.StartAsync();
        slow.Hold();
        await using var fast = await TestReceiver.StartAsync();
        await using var host = Host();
        using var client = await AdminAsync(host);
        await CreateAsync(client, new { name = "slow", url = slow.Url().ToString(), events = new List<string> { "content.published" } });
        await CreateAsync(client, new { name = "fast", url = fast.Url().ToString(), events = new List<string> { "content.published" } });
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Both" }, Token)).Id, cancellationToken: Token);
        await DrainOutboxAsync(host);

        var first = RunNextAsync(host, WebhookDeliveryJob.Queue);
        var second = RunNextAsync(host, WebhookDeliveryJob.Queue);
        await Task.WhenAny(first, second).WaitAsync(Patience, Token);
        await slow.Entered.WaitAsync(Patience, Token);
        await fast.Entered.WaitAsync(Patience, Token);

        slow.Release();
        await Task.WhenAll(first, second).WaitAsync(Patience, Token);
        (slow.Requests.Count, fast.Requests.Count).ShouldBe((1, 1));
    }

    private sealed class Published
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _entries = new();

        public Task Of(Guid entry) => Source(entry).Task;

        private TaskCompletionSource Source(Guid entry) => _entries.GetOrAdd(entry, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        public sealed class Handler(Published published) : IEventHandler<EntryPublishedEvent>
        {
            public ValueTask HandleAsync(EntryPublishedEvent message, CancellationToken cancellationToken)
            {
                published.Source(message.EntryId).TrySetResult();
                return ValueTask.CompletedTask;
            }
        }
    }
}

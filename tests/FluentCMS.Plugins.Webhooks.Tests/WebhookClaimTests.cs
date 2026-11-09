using System.Data.Common;
using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Jobs;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed class WebhookClaimTests(WebhookFixture hosts) : IClassFixture<WebhookFixture>
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    // The send timeout of the tests' host, which is the default, and the margin after it.
    private static readonly TimeSpan SendTimeout = new WebhooksOptions().Timeout;
    private static readonly TimeSpan Abandoned = SendTimeout + WebhookDeliveryJob.AbandonMargin;

    [Fact]
    public async Task AFailedWriteAfterASendNeverSendsThatAttemptAgain()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var fault = new FailFirstResult();
        await using var host = Host().Configure(cms => cms.Services.ConfigureDbContext<PluginDbContext<WebhooksPlugin>>(options => options.AddInterceptors(fault)));
        using var client = await AdminAsync(host);
        await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" } });
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Lost write" }, Token)).Id, cancellationToken: Token);

        await PumpAsync(host);
        fault.Failed.ShouldBeTrue();
        receiver.Requests.Count.ShouldBe(1);
        (await DeliveriesAsync(host)).ShouldHaveSingleItem().State.ShouldBe(DeliveryStates.Pending);

        // The runner retries the failed job, which finds its claim and waits for it to be abandoned.
        host.Time.Advance(TimeSpan.FromMinutes(1));
        (await RunDueJobsAsync(host)).ShouldBe(1);
        receiver.Requests.Count.ShouldBe(1);
        host.Time.Advance(Abandoned);
        (await RunDueJobsAsync(host)).ShouldBe(1);
        receiver.Requests.Count.ShouldBe(1, "the attempt was sent once");
        var taken = (await DeliveriesAsync(host)).ShouldHaveSingleItem();
        (taken.Attempt, taken.State, taken.Error).ShouldBe((1, DeliveryStates.Failed, WebhookDeliveryJob.Interrupted));
        taken.NextRetryAt.ShouldNotBeNull();

        // Its result is not known, so the next attempt follows on the schedule.
        host.Time.Advance(TimeSpan.FromMinutes(1));
        (await RunDueJobsAsync(host)).ShouldBe(1);
        host.Time.Advance(TimeSpan.FromDays(2));
        (await RunDueJobsAsync(host)).ShouldBe(0);
        receiver.Requests.Count.ShouldBe(2);
        receiver.Requests.Select(request => request.Headers[WebhookSender.DeliveryHeader]).Distinct().ShouldHaveSingleItem();
        var attempts = await DeliveriesAsync(host);
        attempts.Select(attempt => (attempt.Attempt, attempt.State)).ShouldBe([(1, DeliveryStates.Failed), (2, DeliveryStates.Succeeded)]);
    }

    [Fact]
    public async Task TwoJobsForOneAttemptSendOnceAndRecordOneRow()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Hold();
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var webhook = Id(await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));
        var attempt = new WebhookAttempt(webhook, Guid.CreateVersion7(), Guid.CreateVersion7(), "content.published", 1, Manual: false, """{"id":1}""");
        var scheduler = host.Services.GetRequiredService<IJobScheduler>();
        for (var copy = 0; copy < 2; copy++)
        {
            await scheduler.EnqueueAsync<WebhookDeliveryJob, WebhookAttempt>(attempt, WebhookDeliveryJob.Options(DefaultSite.Id), Token);
        }

        Task<bool>[] workers = [RunNextAsync(host, WebhookDeliveryJob.Queue), RunNextAsync(host, WebhookDeliveryJob.Queue)];
        (await (await Task.WhenAny(workers)).WaitAsync(Patience, Token)).ShouldBeTrue();
        await receiver.Entered.WaitAsync(Patience, Token);
        receiver.Release();
        (await Task.WhenAll(workers).WaitAsync(Patience, Token)).ShouldAllBe(ran => ran);

        // The second job waits until the claim it found would be abandoned, and then finds the attempt recorded.
        host.Time.Advance(Abandoned);
        (await RunDueJobsAsync(host)).ShouldBe(1);
        await scheduler.EnqueueAsync<WebhookDeliveryJob, WebhookAttempt>(attempt, WebhookDeliveryJob.Options(DefaultSite.Id), Token);
        (await RunDueJobsAsync(host)).ShouldBe(1);

        receiver.Requests.Count.ShouldBe(1);
        var row = (await DeliveriesAsync(host, webhook)).ShouldHaveSingleItem();
        (row.DeliveryId, row.Attempt, row.State).ShouldBe((attempt.DeliveryId, 1, DeliveryStates.Succeeded));
    }

    [Fact]
    public async Task AnAbandonedClaimIsTakenOverAndItsDeliveryGoesOn()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var webhook = Id(await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));
        var crashed = await ClaimAsync(host, webhook, manual: false);
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "After a crash" }, Token)).Id, cancellationToken: Token);

        await PumpAsync(host);
        host.Time.Set(crashed.CreatedAt + SendTimeout + WebhookDeliveryJob.Busy);
        (await RunDueJobsAsync(host)).ShouldBe(1, "the delivery waits until the claim's send must have ended");
        host.Time.Advance(WebhookDeliveryJob.Busy - TimeSpan.FromSeconds(1));
        (await RunDueJobsAsync(host)).ShouldBe(0, "and then at least a little longer each time");
        receiver.Requests.ShouldBeEmpty("the claim of another attempt is in flight until it is abandoned");

        host.Time.Advance(Abandoned);
        (await RunDueJobsAsync(host)).ShouldBe(1);
        var request = receiver.Requests.ShouldHaveSingleItem();
        request.Headers[WebhookSender.DeliveryHeader].ShouldNotBe(crashed.DeliveryId.ToString("D"));
        var taken = (await DeliveriesAsync(host, webhook)).Single(delivery => delivery.Id == crashed.Id);
        (taken.State, taken.Error).ShouldBe((DeliveryStates.Failed, WebhookDeliveryJob.Interrupted));
        taken.NextRetryAt.ShouldNotBeNull();

        host.Time.Advance(WebhookDeliveryJob.Retries[0] - TimeSpan.FromSeconds(1));
        (await RunDueJobsAsync(host)).ShouldBe(0, "its next attempt follows on the schedule");
        host.Time.Advance(TimeSpan.FromSeconds(1));
        (await RunDueJobsAsync(host)).ShouldBe(1);
        receiver.Requests.Count.ShouldBe(2);
        receiver.Requests[1].Headers[WebhookSender.DeliveryHeader].ShouldBe(crashed.DeliveryId.ToString("D"));
        receiver.Requests[1].Body.ShouldBe(crashed.RequestBody);
        var deliveries = await DeliveriesAsync(host, webhook);
        deliveries.Where(delivery => delivery.DeliveryId == crashed.DeliveryId).Select(delivery => (delivery.Attempt, delivery.State))
            .ShouldBe([(1, DeliveryStates.Failed), (2, DeliveryStates.Succeeded)]);
        deliveries.ShouldNotContain(delivery => delivery.State == DeliveryStates.Pending);
    }

    [Fact]
    public async Task AnAbandonedPingIsDeadAndNotTriedAgain()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var webhook = Id(await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));
        var crashed = await ClaimAsync(host, webhook, manual: true);
        host.Time.Advance(Abandoned);

        using var ping = await client.PostAsync(new Uri($"/api/management/v1/webhooks/{webhook:D}/ping", UriKind.Relative), null, Token);
        ping.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await PumpAsync(host);
        host.Time.Advance(TimeSpan.FromDays(2));
        (await RunDueJobsAsync(host)).ShouldBe(0);

        receiver.Requests.ShouldHaveSingleItem().Headers[WebhookSender.EventHeader].ShouldBe("ping");
        var taken = (await DeliveriesAsync(host, webhook)).Single(delivery => delivery.Id == crashed.Id);
        (taken.State, taken.Error, taken.NextRetryAt).ShouldBe((DeliveryStates.Dead, WebhookDeliveryJob.Interrupted, (DateTimeOffset?)null));
    }

    // The claim of a worker that stopped before it recorded the result.
    private static async Task<WebhookDelivery> ClaimAsync(FluentCmsTestHost host, Guid webhook, bool manual)
    {
        var claim = new WebhookDelivery
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = Guid.CreateVersion7(),
            WebhookId = webhook,
            EventId = Guid.CreateVersion7(),
            Event = manual ? "ping" : "content.published",
            Attempt = 1,
            State = DeliveryStates.Pending,
            RequestBody = """{"crashed":true}""",
            Manual = manual,
            CreatedAt = host.Time.GetUtcNow(),
        };
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
        database.Add(claim);
        await database.SaveChangesAsync(Token);
        return claim;
    }

    // Fails the first write of a result, after its attempt was sent.
    private sealed class FailFirstResult : DbCommandInterceptor
    {
        private int _failed;

        public bool Failed => Volatile.Read(ref _failed) == 1;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"cms_webhooks_delivery\"", StringComparison.Ordinal)
                && command.CommandText.Contains("\"status_code\"", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _failed, 1) == 0)
            {
                throw new InvalidOperationException("The database went away.");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

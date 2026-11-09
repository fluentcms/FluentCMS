using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed class WebhookDeadTests(WebhookFixture hosts, DisablingWebhookFixture disabling) : IClassFixture<WebhookFixture>, IClassFixture<DisablingWebhookFixture>
{
    [Fact]
    public async Task ADeadDeliveryIsAnActivityRowAndLeavesTheWebhookEnabledByDefault()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Status = 500;
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var id = Id(await CreateAsync(client, new { name = "flaky", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));

        await PublishAsync(host, "Dead");
        await RetryUntilDeadAsync(host);
        using (var ping = await client.PostAsync(new Uri($"/api/management/v1/webhooks/{id:D}/ping", UriKind.Relative), null, Token))
        {
            ping.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await PumpAsync(host);

        var delivery = (await DeliveriesAsync(host, id)).First(attempt => attempt.State == DeliveryStates.Dead && !attempt.Manual);
        var dead = (await ActivityAsync(client, "webhook.delivery_dead", id)).ShouldHaveSingleItem("a ping that fails is tried once and raises nothing");
        (dead["targetId"]!.GetValue<string>(), dead["targetLabel"]!.GetValue<string>()).ShouldBe((id.ToString("D"), "flaky"));
        var details = dead["details"]!;
        (details["deliveryId"]!.GetValue<string>(), details["event"]!.GetValue<string>(), details["attempts"]!.GetValue<string>())
            .ShouldBe((delivery.DeliveryId.ToString("D"), "content.published", "6"));
        (await EnabledAsync(client, id)).ShouldBeTrue();
        (await ActivityAsync(client, "webhook.disabled", id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AWebhookWhoseAttemptsAllFailedForDisableAfterIsDisabledWhenADeliveryDies()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Status = 500;
        var host = await disabling.HostAsync();
        using var client = await AdminAsync(host);
        var id = Id(await CreateAsync(client, new { name = "gone", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));

        await PublishAsync(host, "First");
        await RetryUntilDeadAsync(host);
        var failingSince = (await DeliveriesAsync(host, id))[0].CreatedAt;

        (await EnabledAsync(client, id)).ShouldBeTrue("its attempts have failed for about a day, less than DisableAfter");

        host.Time.Advance(TimeSpan.FromDays(1));
        await PublishAsync(host, "Second");
        await RetryUntilDeadAsync(host);

        (await EnabledAsync(client, id)).ShouldBeFalse();
        (await ActivityAsync(client, "webhook.delivery_dead", id)).Count.ShouldBe(2);
        var disabled = (await ActivityAsync(client, "webhook.disabled", id)).ShouldHaveSingleItem();
        (disabled["targetId"]!.GetValue<string>(), disabled["details"]!["failingSince"]!.GetValue<string>()).ShouldBe((id.ToString("D"), failingSince.ToString("O")));

        await PublishAsync(host, "Third");
        receiver.Requests.Count.ShouldBe(12, "a disabled webhook gets no delivery");
    }

    [Fact]
    public async Task ASuccessInsideTheWindowKeepsTheWebhookEnabled()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Status = 500;
        await using var host = Host().WithServices(services => services.PostConfigure<WebhooksOptions>(options => options.DisableAfter = TimeSpan.FromDays(1)));
        using var client = await AdminAsync(host);
        var id = Id(await CreateAsync(client, new { name = "recovers", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));

        await PublishAsync(host, "Recovers");
        foreach (var delay in WebhookDeliveryJob.Retries.SkipLast(1))
        {
            host.Time.Advance(delay);
            await PumpAsync(host);
        }

        receiver.Status = 200;
        using (var ping = await client.PostAsync(new Uri($"/api/management/v1/webhooks/{id:D}/ping", UriKind.Relative), null, Token))
        {
            ping.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await PumpAsync(host);
        receiver.Status = 500;
        host.Time.Advance(WebhookDeliveryJob.Retries[^1]);
        await PumpAsync(host);

        (await ActivityAsync(client, "webhook.delivery_dead", id)).ShouldHaveSingleItem();
        (await EnabledAsync(client, id)).ShouldBeTrue("the ping succeeded less than DisableAfter before the delivery died");
    }

    [Fact]
    public async Task ChangingAWebhookStartsItsFailingTimeAgain()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Status = 500;
        var host = await disabling.HostAsync();
        using var client = await AdminAsync(host);
        var url = receiver.Url().ToString();
        var id = Id(await CreateAsync(client, new { name = "moved", url, events = new List<string> { "content.published" } }));
        await PublishAsync(host, "Before");
        await RetryUntilDeadAsync(host);
        host.Time.Advance(TimeSpan.FromDays(1));

        using (var changed = await client.PutAsJsonAsync(new Uri($"/api/management/v1/webhooks/{id:D}", UriKind.Relative), new { name = "moved", url = receiver.Url("/new").ToString(), events = new List<string> { "content.published" } }, Token))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await PublishAsync(host, "After");
        await RetryUntilDeadAsync(host);

        (await EnabledAsync(client, id)).ShouldBeTrue("its attempts have failed for about a day since the change, less than DisableAfter");
        (await ActivityAsync(client, "webhook.disabled", id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ALastAttemptTakenOverAsAbandonedRaisesTheDeadEvent()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var host = await hosts.HostAsync();
        using var client = await AdminAsync(host);
        var id = Id(await CreateAsync(client, new { name = "crashed", url = receiver.Url().ToString(), events = new List<string> { "content.published" } }));
        var claim = new WebhookDelivery
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = Guid.CreateVersion7(),
            WebhookId = id,
            EventId = Guid.CreateVersion7(),
            Event = "content.published",
            Attempt = WebhookDeliveryJob.Retries.Count + 1,
            State = DeliveryStates.Pending,
            RequestBody = "{}",
            CreatedAt = host.Time.GetUtcNow(),
        };
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
            database.Add(claim);
            await database.SaveChangesAsync(Token);
        }

        host.Time.Advance(new WebhooksOptions().Timeout + WebhookDeliveryJob.AbandonMargin);
        using (var ping = await client.PostAsync(new Uri($"/api/management/v1/webhooks/{id:D}/ping", UriKind.Relative), null, Token))
        {
            ping.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await PumpAsync(host);

        (await DeliveriesAsync(host, id)).Single(delivery => delivery.Id == claim.Id).State.ShouldBe(DeliveryStates.Dead);
        var dead = (await ActivityAsync(client, "webhook.delivery_dead", id)).ShouldHaveSingleItem();
        (dead["details"]!["deliveryId"]!.GetValue<string>(), dead["details"]!["attempts"]!.GetValue<string>()).ShouldBe((claim.DeliveryId.ToString("D"), "6"));
    }

    [Fact]
    public async Task DisableAfterIsAtMostTheRetentionOfTheLog()
    {
        await using var host = Host().WithServices(services => services.PostConfigure<WebhooksOptions>(options => options.DisableAfter = TimeSpan.FromDays(31)));

        Should.Throw<OptionsValidationException>(() => host.Services.GetRequiredService<IOptions<WebhooksOptions>>().Value).Message.ShouldContain("DisableAfter");
    }

    private static async Task PublishAsync(FluentCmsTestHost host, string title)
    {
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = title }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);
    }

    private static async Task RetryUntilDeadAsync(FluentCmsTestHost host)
    {
        foreach (var delay in WebhookDeliveryJob.Retries)
        {
            host.Time.Advance(delay);
            await PumpAsync(host);
        }
    }

    private static async Task<bool> EnabledAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(new Uri($"/api/management/v1/webhooks/{id:D}", UriKind.Relative), Token);
        return JsonNode.Parse(await TextAsync(response))!["enabled"]!.GetValue<bool>();
    }

    private static async Task<List<JsonNode>> ActivityAsync(HttpClient client, string action, Guid webhook)
    {
        using var response = await client.GetAsync(new Uri($"/api/management/v1/activity?action={action}&targetId={webhook:D}", UriKind.Relative), Token);
        return [.. JsonNode.Parse(await TextAsync(response))!["items"]!.AsArray().Select(item => item!)];
    }
}

// Disables a webhook whose attempts have all failed for two days, more than the day its first delivery takes to die.
public sealed class DisablingWebhookFixture() : WebhookFixture(Host().WithServices(services => services.PostConfigure<WebhooksOptions>(options => options.DisableAfter = TimeSpan.FromDays(2))));

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Webhooks.Tests.WebhookTesting;

namespace FluentCMS.Plugins.Webhooks.Tests;

public sealed class WebhookDeliveryTests
{
    private const string Secret = "a-secret-of-the-receiver-1234";

    [Fact]
    public async Task PublishingASubscribedEntrySendsOneSignedPostWithThePayload()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var host = Host();
        using var client = await AdminAsync(host);
        await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" }, types = new List<string> { "article" }, secret = Secret });
        var user = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, "editor", Token);
        var articles = host.As(user).Content<Article>();

        var article = await articles.CreateAsync(new Article { Title = "Hello" }, Token);
        await articles.PublishAsync(article.Id, cancellationToken: Token);
        await PumpAsync(host);

        var request = receiver.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("POST");
        request.Headers["Content-Type"].ShouldStartWith("application/json");
        request.Headers[WebhookSender.EventHeader].ShouldBe("content.published");
        var delivery = (await DeliveriesAsync(host)).ShouldHaveSingleItem();
        request.Headers[WebhookSender.DeliveryHeader].ShouldBe(delivery.DeliveryId.ToString("D"));
        Verifies(request.Headers[WebhookSender.SignatureHeader], Secret, request.Body).ShouldBeTrue();
        Verifies(request.Headers[WebhookSender.SignatureHeader], "another-secret-of-16", request.Body).ShouldBeFalse();

        var body = JsonNode.Parse(request.Body)!;
        body["event"]!.GetValue<string>().ShouldBe("content.published");
        Guid.Parse(body["id"]!.GetValue<string>()).ShouldBe(delivery.EventId);
        Guid.Parse(body["site"]!.GetValue<string>()).ShouldBe(DefaultSite.Id);
        body["origin"]!.GetValue<string>().ShouldBe("admin");
        body["occurredAt"].ShouldNotBeNull();
        body["actor"]!["kind"]!.GetValue<string>().ShouldBe("adminUser");
        Guid.Parse(body["actor"]!["id"]!.GetValue<string>()).ShouldBe(user.Id);
        var data = body["data"]!;
        data["type"]!.GetValue<string>().ShouldBe("article");
        Guid.Parse(data["documentId"]!.GetValue<string>()).ShouldBe(article.Id);
        data["locale"]!.GetValue<string>().ShouldBe("", "the type is not localized");
        data["status"]!.GetValue<string>().ShouldBe("published");
        data["version"]!.GetValue<int>().ShouldBeGreaterThan(0);
        body["links"]!["delivery"]!.GetValue<string>().ShouldBe($"api/delivery/v1/article/{article.Id:D}");
        delivery.State.ShouldBe(DeliveryStates.Succeeded);
        delivery.StatusCode.ShouldBe(200);
        delivery.RequestBody.ShouldBe(request.Body);
    }

    [Fact]
    public async Task AnEventOfAnotherNameTypeOrSiteSendsNothing()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var host = Host();
        using var client = await AdminAsync(host);
        var url = receiver.Url().ToString();
        await CreateAsync(client, new { name = "articles", url, events = new List<string> { "content.published" }, types = new List<string> { "article" } });
        var other = await CreateAsync(client, new { name = "other site", url, events = new List<string> { "content.published", "content.draft_saved" } });
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<WebhooksPlugin>>();
            var webhook = await database.Set<Webhook>().FindAsync([Id(other)], Token);
            webhook!.SiteId = Guid.CreateVersion7();
            await database.SaveChangesAsync(Token);
        }

        var admin = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token));
        await admin.Content<Article>().CreateAsync(new Article { Title = "Only a draft" }, Token);
        var page = await admin.Content<Page>().CreateAsync(new Page { Title = "A page" }, Token);
        await admin.Content<Page>().PublishAsync(page.Id, cancellationToken: Token);
        await PumpAsync(host);

        receiver.Requests.ShouldBeEmpty();
        (await DeliveriesAsync(host)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AFailingReceiverIsRetriedOnTheScheduleAndThenTheDeliveryIsDead()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Status = 503;
        await using var host = Host();
        using var client = await AdminAsync(host);
        await CreateAsync(client, new { name = "flaky", url = receiver.Url().ToString(), events = new List<string> { "content.published" } });
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Flaky" }, Token)).Id, cancellationToken: Token);

        await PumpAsync(host);
        TimeSpan[] schedule = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24)];
        foreach (var delay in schedule)
        {
            host.Time.Advance(delay - TimeSpan.FromSeconds(1));
            (await RunDueJobsAsync(host)).ShouldBe(0, $"the attempt after {delay} is not due yet");
            host.Time.Advance(TimeSpan.FromSeconds(1));
            (await RunDueJobsAsync(host)).ShouldBe(1, $"the attempt after {delay} is due");
        }

        host.Time.Advance(TimeSpan.FromDays(3));
        (await RunDueJobsAsync(host)).ShouldBe(0);

        var attempts = await DeliveriesAsync(host);
        attempts.Select(attempt => attempt.Attempt).ShouldBe(Enumerable.Range(1, 6));
        attempts.Select(attempt => attempt.DeliveryId).Distinct().ShouldHaveSingleItem();
        attempts.ShouldAllBe(attempt => attempt.StatusCode == 503 && attempt.DurationMs >= 0 && attempt.ResponseExcerpt == "receiver failed");
        attempts.Take(5).ShouldAllBe(attempt => attempt.State == DeliveryStates.Failed);
        attempts.Take(5).Select(attempt => attempt.NextRetryAt!.Value - attempt.CreatedAt).ShouldBe(schedule);
        attempts[5].State.ShouldBe(DeliveryStates.Dead);
        attempts[5].NextRetryAt.ShouldBeNull();
        receiver.Requests.Count.ShouldBe(6);
        receiver.Requests.Select(request => request.Headers[WebhookSender.DeliveryHeader]).Distinct().ShouldHaveSingleItem();
        receiver.Requests.Select(request => request.Body).Distinct().ShouldHaveSingleItem();

        using var log = await client.GetAsync(new Uri($"/api/management/v1/webhooks/{attempts[0].WebhookId:D}/deliveries?pageSize=10", UriKind.Relative), Token);
        var page = JsonNode.Parse(await TextAsync(log))!;
        page["total"]!.GetValue<int>().ShouldBe(6);
        var newest = page["items"]!.AsArray()[0]!;
        (newest["attempt"]!.GetValue<int>(), newest["state"]!.GetValue<string>(), newest["statusCode"]!.GetValue<int>()).ShouldBe((6, "dead", 503));
        newest["durationMs"].ShouldNotBeNull();
    }

    [Fact]
    public async Task AWebhookDisabledBeforeItsRetryGetsNoMoreAttemptsAndItsDeliveryIsDead()
    {
        await using var receiver = await TestReceiver.StartAsync();
        receiver.Status = 500;
        await using var host = Host();
        using var client = await AdminAsync(host);
        var url = receiver.Url().ToString();
        var webhook = await CreateAsync(client, new { name = "paused", url, events = new List<string> { "content.published" } });
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Paused" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);

        using var disabled = await client.PutAsJsonAsync(new Uri($"/api/management/v1/webhooks/{Id(webhook):D}", UriKind.Relative), new { name = "paused", url, events = new List<string> { "content.published" }, enabled = false }, Token);
        disabled.StatusCode.ShouldBe(HttpStatusCode.OK, await TextAsync(disabled));
        host.Time.Advance(TimeSpan.FromMinutes(1));
        (await RunDueJobsAsync(host)).ShouldBe(1);

        receiver.Requests.Count.ShouldBe(1);
        var attempt = (await DeliveriesAsync(host)).ShouldHaveSingleItem();
        (attempt.State, attempt.NextRetryAt).ShouldBe((DeliveryStates.Dead, (DateTimeOffset?)null));
    }

    [Fact]
    public async Task RedeliverSendsADeliveryAgainAndPingSendsATestEventOnceEach()
    {
        await using var receiver = await TestReceiver.StartAsync();
        await using var host = Host();
        using var client = await AdminAsync(host);
        var webhook = await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" } });
        var root = $"/api/management/v1/webhooks/{Id(webhook):D}";
        var articles = host.As(await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
        await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Again" }, Token)).Id, cancellationToken: Token);
        await PumpAsync(host);
        var first = (await DeliveriesAsync(host)).ShouldHaveSingleItem();

        using var redeliver = await client.PostAsync(new Uri($"{root}/deliveries/{first.DeliveryId:D}/redeliver", UriKind.Relative), null, Token);
        redeliver.StatusCode.ShouldBe(HttpStatusCode.Accepted, await TextAsync(redeliver));
        await PumpAsync(host);
        receiver.Status = 500;
        using var ping = await client.PostAsync(new Uri($"{root}/ping", UriKind.Relative), null, Token);
        ping.StatusCode.ShouldBe(HttpStatusCode.Accepted, await TextAsync(ping));
        await PumpAsync(host);
        host.Time.Advance(TimeSpan.FromDays(2));
        (await RunDueJobsAsync(host)).ShouldBe(0, "a manual delivery is tried once");

        var deliveries = await DeliveriesAsync(host);
        deliveries.Count.ShouldBe(3);
        var redelivered = Id(JsonNode.Parse(await TextAsync(redeliver))!, "deliveryId");
        var pingedId = Id(JsonNode.Parse(await TextAsync(ping))!, "deliveryId");
        var again = deliveries.Single(delivery => delivery.DeliveryId == redelivered);
        (again.State, again.EventId, again.RequestBody).ShouldBe((DeliveryStates.Succeeded, first.EventId, first.RequestBody));
        again.DeliveryId.ShouldNotBe(first.DeliveryId);
        var pinged = deliveries.Single(delivery => delivery.DeliveryId == pingedId);
        (pinged.Event, pinged.State, pinged.StatusCode, pinged.NextRetryAt).ShouldBe(("ping", DeliveryStates.Dead, (int?)500, (DateTimeOffset?)null));
        receiver.Requests.Count.ShouldBe(3);
        receiver.Requests[2].Headers[WebhookSender.EventHeader].ShouldBe("ping");
        JsonNode.Parse(receiver.Requests[2].Body)!["event"]!.GetValue<string>().ShouldBe("ping");

        using var missing = await client.PostAsync(new Uri($"{root}/deliveries/{Guid.CreateVersion7():D}/redeliver", UriKind.Relative), null, Token);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(missing)).ShouldBe(WebhookErrorCodes.DeliveryNotFound);

        using var deleted = await client.DeleteAsync(new Uri(root, UriKind.Relative), Token);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DeliveriesAsync(host)).ShouldBeEmpty("the log goes with its webhook");
    }

    [Fact]
    public async Task AnEventCommittedJustBeforeACrashIsDeliveredAfterTheRestart()
    {
        await using var receiver = await TestReceiver.StartAsync();
        var directory = Directory.CreateTempSubdirectory("fluentcms-webhooks-");
        try
        {
            var file = Path.Combine(directory.FullName, "cms.db");
            await using (var before = Host().UseSqlite(file))
            {
                using var client = await AdminAsync(before);
                await CreateAsync(client, new { name = "builds", url = receiver.Url().ToString(), events = new List<string> { "content.published" } });
                var articles = before.As(await before.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token)).Content<Article>();
                await articles.PublishAsync((await articles.CreateAsync(new Article { Title = "Before the crash" }, Token)).Id, cancellationToken: Token);
            }

            receiver.Requests.ShouldBeEmpty();

            await using var after = Host().UseSqlite(file);
            await PumpAsync(after);

            var request = receiver.Requests.ShouldHaveSingleItem();
            request.Headers[WebhookSender.EventHeader].ShouldBe("content.published");
            JsonNode.Parse(request.Body)!["data"]!["status"]!.GetValue<string>().ShouldBe("published");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheSignatureIsTheHmacOfTheTimestampAndTheBody()
    {
        var signature = WebhookSigning.Signature("secret-of-the-hook", 1_700_000_000, """{"id":1}""");

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret-of-the-hook"), Encoding.UTF8.GetBytes("""1700000000.{"id":1}""")));
        signature.ShouldBe($"t=1700000000,v1={expected}");
    }

    // What a receiver does: recompute the HMAC over t + "." + body and compare in constant time.
    private static bool Verifies(string header, string secret, string body)
    {
        var parts = header.Split(',').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => part[1]);
        long.Parse(parts["t"], CultureInfo.InvariantCulture).ShouldBeGreaterThan(0);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(parts["t"] + "." + body));
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts["v1"]));
    }
}

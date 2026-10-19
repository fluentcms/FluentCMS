using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Webhooks;

public sealed class WebhookDeliveryTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_publish_sends_one_signed_request_naming_the_set_and_its_items(string provider)
    {
        await using var receiver = await WebhookReceiver.StartAsync();
        await using var site = await Site.StartAsync(provider, receiver);
        var added = await site.AddAsync(receiver.Url, WebhookEvents.ChangeSetPublished, WebhookEvents.ChangeSetReverted);

        var (published, type) = await site.PublishTypeAsync("product");
        var received = Assert.Single(await receiver.WaitForAsync(1));
        await site.UntilDeliveredAsync(added.Subscription.Id, 1);

        Assert.True(Verifies(received, added.Secret));
        Assert.False(Verifies(received, WebhookSecrets.Generate()));
        using var body = JsonDocument.Parse(received.Body);
        Assert.Equal(WebhookEvents.ChangeSetPublished, body.RootElement.GetProperty("type").GetString());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(published.Id.ToString(), data.GetProperty("changeSet").GetProperty("id").GetString());
        Assert.Equal($"/sets/{published.Id}", data.GetProperty("changeSet").GetProperty("link").GetString());
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(("schema", type.Id.ToString(), "create", 1), (item.GetProperty("kind").GetString(), item.GetProperty("id").GetString(), item.GetProperty("operation").GetString(), item.GetProperty("version").GetInt32()));
        Assert.False(data.TryGetProperty("reverted", out _));

        var delivery = Assert.Single(await site.DeliveriesAsync(added.Subscription.Id));
        Assert.Equal((200, 1, received.Headers["webhook-id"]), (delivery.StatusCode, delivery.Attempt, delivery.DeliveryId));
        Assert.False(await site.StoresTextAsync(added.Secret[WebhookSecrets.Prefix.Length..]));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revert_sends_the_reverted_event_naming_both_sets(string provider)
    {
        await using var receiver = await WebhookReceiver.StartAsync();
        await using var site = await Site.StartAsync(provider, receiver);
        await site.AddAsync(receiver.Url, WebhookEvents.ChangeSetReverted);

        var (published, _) = await site.PublishTypeAsync("product");
        var revert = await site.RevertAsync(published.Id);
        var received = Assert.Single(await receiver.WaitForAsync(1));

        using var body = JsonDocument.Parse(received.Body);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(WebhookEvents.ChangeSetReverted, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(revert.Id.ToString(), data.GetProperty("changeSet").GetProperty("id").GetString());
        Assert.Equal(published.Id.ToString(), data.GetProperty("reverted").GetProperty("id").GetString());
        Assert.Equal("delete", Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("operation").GetString());
    }

    // Each attempt sends the same request under the same id, so a receiver can tell a retry from a new event.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_failing_endpoint_is_retried_and_every_attempt_is_logged(string provider)
    {
        await using var receiver = await WebhookReceiver.StartAsync();
        receiver.Answer(500, 503);
        await using var site = await Site.StartAsync(provider, receiver);
        var added = await site.AddAsync(receiver.Url, WebhookEvents.ChangeSetPublished);

        await site.PublishTypeAsync("product");
        var received = await receiver.WaitForAsync(3);
        await site.UntilDeliveredAsync(added.Subscription.Id, 3);

        Assert.Single(received.Select(request => request.Headers["webhook-id"]).Distinct());
        Assert.Single(received.Select(request => request.Body).Distinct());
        var deliveries = (await site.DeliveriesAsync(added.Subscription.Id)).Reverse().ToList();
        Assert.Equal([(1, 500), (2, 503), (3, 200)], deliveries.Select(delivery => (delivery.Attempt, delivery.StatusCode ?? 0)));
        Assert.Contains("answered 500", deliveries[0].Error);
        Assert.Null(deliveries[2].Error);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_redirect_is_not_followed_and_counts_as_a_failure(string provider)
    {
        await using var receiver = await WebhookReceiver.StartAsync();
        receiver.Answer(302);
        await using var site = await Site.StartAsync(provider, receiver, maxAttempts: 1);
        var added = await site.AddAsync(receiver.Url, WebhookEvents.ChangeSetPublished);

        await site.PublishTypeAsync("product");
        await site.UntilDeliveredAsync(added.Subscription.Id, 1);
        var job = await site.UntilJobEndsAsync(added.Subscription.Id);

        var delivery = Assert.Single(await site.DeliveriesAsync(added.Subscription.Id));
        Assert.Equal(302, delivery.StatusCode);
        Assert.Single(receiver.Requests);
        Assert.Equal(JobState.Dead, job.State);
    }

    // The allow-list is read when the host starts, so the endpoint's host is left out of a host started later.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_host_taken_off_the_allow_list_is_refused_at_sending_and_not_retried(string provider)
    {
        await using var receiver = await WebhookReceiver.StartAsync();
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        Guid subscription;
        Guid owner;
        await using (var site = await Site.StartAsync(install, database, receiver))
        {
            owner = site.Owner;
            subscription = (await site.AddAsync(receiver.Url, WebhookEvents.ChangeSetPublished)).Subscription.Id;
        }

        await using var later = await Site.StartAsync(install, database, receiver, allowReceiver: false, owner: owner);
        await later.PublishTypeAsync("product");
        await later.UntilDeliveredAsync(subscription, 1);
        var job = await later.UntilJobEndsAsync(subscription);

        Assert.Empty(receiver.Requests);
        Assert.Equal((JobState.Failed, 1), (job.State, job.Attempts));
        Assert.Contains("no longer on the outbound allow-list", Assert.Single(await later.DeliveriesAsync(subscription)).Error);
    }

    // Deliveries are queued in the publish's unit of work, so what is queued is known as soon as the publish returns.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_disabled_subscription_and_one_that_does_not_want_the_event_get_nothing(string provider)
    {
        await using var receiver = await WebhookReceiver.StartAsync();
        await using var site = await Site.StartAsync(provider, receiver);
        await site.AddAsync(receiver.Url, WebhookEvents.ChangeSetReverted);
        var disabled = await site.AddAsync(receiver.Url, WebhookEvents.ChangeSetPublished);
        await site.AsOwnerAsync<IWebhookDirectory, WebhookSubscription>(directory => directory.DisableAsync(disabled.Subscription.Id));

        await site.PublishTypeAsync("product");

        Assert.Equal(0, await site.QueryAsync(context => context.Jobs.CountAsync(row => row.Type == "webhook.deliver")));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_test_delivery_is_sent_and_logged_and_needs_an_enabled_webhook(string provider)
    {
        await using var receiver = await WebhookReceiver.StartAsync();
        await using var site = await Site.StartAsync(provider, receiver);
        var added = await site.AddAsync(receiver.Url, WebhookEvents.ChangeSetPublished);

        Assert.True((await site.AsOwnerAsync<IWebhookDirectory>(directory => directory.SendTestAsync(added.Subscription.Id))).IsSuccess);
        var received = Assert.Single(await receiver.WaitForAsync(1));
        await site.AsOwnerAsync<IWebhookDirectory, WebhookSubscription>(directory => directory.DisableAsync(added.Subscription.Id));
        var refused = await site.AsOwnerAsync<IWebhookDirectory>(directory => directory.SendTestAsync(added.Subscription.Id));

        using var body = JsonDocument.Parse(received.Body);
        Assert.Equal(WebhookEvents.Test, body.RootElement.GetProperty("type").GetString());
        Assert.True(Verifies(received, added.Secret));
        Assert.Equal(WebhookErrorCodes.Disabled, Assert.Single(refused.Errors).Code);
        Assert.Equal(1, await site.QueryAsync(context => context.AuditEvents.CountAsync(row => row.Operation == "webhook.test_requested")));
    }

    // As a receiver would check it: HMAC-SHA256 of "id.timestamp.body" with the secret's bytes, in base64 after "v1,".
    private static bool Verifies(WebhookReceiver.Received request, string secret)
    {
        var key = Convert.FromBase64String(secret["whsec_".Length..]);
        var signed = Encoding.UTF8.GetBytes($"{request.Headers["webhook-id"]}.{request.Headers["webhook-timestamp"]}.{request.Body}");
        var expected = "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, signed));
        return long.TryParse(request.Headers["webhook-timestamp"], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            && request.Headers["webhook-signature"].Split(' ').Contains(expected);
    }

    // A running host with its job queue, a signed-in owner and the receiver on its allow-list, unless a test takes it off.
    private sealed class Site : IAsyncDisposable
    {
        private readonly AdminHost _host;
        private readonly IDisposable? _install;
        private readonly TemporaryDatabase? _database;

        private Site(AdminHost host, Guid owner, IDisposable? install, TemporaryDatabase? database)
        {
            _host = host;
            Owner = owner;
            _install = install;
            _database = database;
        }

        public Guid Owner { get; }

        public static async Task<Site> StartAsync(string provider, WebhookReceiver receiver, int maxAttempts = 3)
        {
            var install = new TemporaryInstall();
            var database = await TemporaryDatabase.CreateAsync(provider);
            var host = await AdminHost.StartAsync(install, settings: Settings(database, receiver, allowReceiver: true, maxAttempts));
            return new Site(host, await host.CreateFirstAdministratorAsync(), install, database);
        }

        public static async Task<Site> StartAsync(
            TemporaryInstall install,
            TemporaryDatabase database,
            WebhookReceiver receiver,
            bool allowReceiver = true,
            Guid? owner = null)
        {
            var host = await AdminHost.StartAsync(install, settings: Settings(database, receiver, allowReceiver, maxAttempts: 3));
            return new Site(host, owner ?? await host.CreateFirstAdministratorAsync(), install: null, database: null);
        }

        public Task<AddedWebhookSubscription> AddAsync(Uri url, params string[] events)
        {
            return AsOwnerAsync<IWebhookDirectory, AddedWebhookSubscription>(directory => directory.AddAsync(new NewWebhookSubscription(url.ToString(), events)));
        }

        public async Task<(ChangeSet Published, ContentType Type)> PublishTypeAsync(string slug)
        {
            var type = await AsOwnerAsync<IContentTypeDirectory, ContentType>(directory =>
                directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([new FieldDefinition("title", "text")]))));
            var published = await AsOwnerAsync<IChangeSetDirectory, ChangeSet>(async directory =>
            {
                var opened = (await directory.OpenAsync(new NewChangeSet("Add the type.", []))).Value;
                await directory.AddOperationAsync(opened.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value);
                await directory.TransitionAsync(opened.Id, ChangeSetState.ChecksRunning);
                await directory.TransitionAsync(opened.Id, ChangeSetState.Approved);
                return await directory.PublishAsync(opened.Id);
            });
            return (published, type);
        }

        public Task<ChangeSet> RevertAsync(Guid changeSetId)
        {
            return AsOwnerAsync<IChangeSetDirectory, ChangeSet>(async directory =>
            {
                var revert = (await directory.RevertAsync(changeSetId, "Added too early.")).Value;
                await directory.TransitionAsync(revert.Id, ChangeSetState.ChecksRunning);
                await directory.TransitionAsync(revert.Id, ChangeSetState.Approved);
                return await directory.PublishAsync(revert.Id);
            });
        }

        public async Task<IReadOnlyList<WebhookDelivery>> DeliveriesAsync(Guid subscriptionId)
        {
            return await AsOwnerAsync<IWebhookDirectory, IReadOnlyList<WebhookDelivery>>(directory => directory.DeliveriesAsync(subscriptionId, 100));
        }

        public async Task UntilDeliveredAsync(Guid subscriptionId, int attempts)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while ((await DeliveriesAsync(subscriptionId)).Count < attempts)
            {
                Assert.True(DateTime.UtcNow < deadline, "The deliveries were not logged in time.");
                await Task.Delay(50);
            }
        }

        public async Task<Job> UntilJobEndsAsync(Guid subscriptionId)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                var job = (await AsOwnerAsync<IJobQueue, IReadOnlyList<Job>>(async queue =>
                    Core.Results.Result<IReadOnlyList<Job>>.Success(await queue.ListForSubjectAsync(new JobSubject("webhook_subscription", subscriptionId))))).Single();
                if (job.State is JobState.Succeeded or JobState.Failed or JobState.Dead)
                {
                    return job;
                }

                Assert.True(DateTime.UtcNow < deadline, "The delivery did not end in time.");
                await Task.Delay(50);
            }
        }

        public async Task<T> AsOwnerAsync<TService, T>(Func<TService, Task<Core.Results.Result<T>>> action)
            where TService : notnull
        {
            var result = await _host.AsUserAsync(Owner, action);
            Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
            return result.Value;
        }

        public Task<Core.Results.Result> AsOwnerAsync<TService>(Func<TService, Task<Core.Results.Result>> action)
            where TService : notnull
        {
            return _host.AsUserAsync(Owner, action);
        }

        public async Task<T> QueryAsync<T>(Func<FluentCmsDbContext, Task<T>> query)
        {
            await using var scope = _host.App.Services.CreateAsyncScope();
            return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
        }

        public Task<bool> StoresTextAsync(string text)
        {
            return _database!.StoresTextAsync(_host.App.Services, text);
        }

        public async ValueTask DisposeAsync()
        {
            await _host.DisposeAsync();
            if (_database is not null)
            {
                await _database.DisposeAsync();
            }

            _install?.Dispose();
        }

        private static Dictionary<string, string?> Settings(TemporaryDatabase database, WebhookReceiver receiver, bool allowReceiver, int maxAttempts)
        {
            var settings = new Dictionary<string, string?>(database.Configuration)
            {
                ["JobQueue:PollingInterval"] = "00:00:00.050",
                ["JobQueue:RetryDelay"] = "00:00:00.050",
                ["Webhooks:MaxAttempts"] = maxAttempts.ToString(CultureInfo.InvariantCulture),
                ["Outbound:AllowedHosts:0"] = allowReceiver ? receiver.Host : "example.com",
            };
            return settings;
        }
    }
}

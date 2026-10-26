using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Webhooks;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Infrastructure.Webhooks;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.ChangeSets;

// The contract every publish listener meets, built in or not (EXT-N1): it takes a publish and a revert's publish without failing, and
// what it records is in the publish's unit of work, so a publish that rolls back leaves none of it. Derive one class per listener: pick
// it from the registered ones, register it in AddListener if it lives outside the product, and seed what it reacts to in SeedAsync.
[ConformanceFor(typeof(IChangeSetEvents))]
public abstract class ChangeSetEventsConformanceTests
{
    protected abstract IChangeSetEvents Listener(IServiceProvider services);

    protected virtual void AddListener(IServiceCollection services)
    {
    }

    protected virtual Task SeedAsync(FluentCmsDbContext context)
    {
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task It_takes_a_publish_and_a_reverts_publish_without_failing(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider, AddListener, SeedAsync);
        var (published, reverted) = Sets();

        await using var scope = scenario.Services.CreateAsyncScope();
        var listener = Listener(scope.ServiceProvider);
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

        await listener.PublishedAsync(published, null);
        await listener.PublishedAsync(published, reverted);
        await work.CommitAsync();
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task What_it_records_rolls_back_with_the_publish(string provider)
    {
        await using var scenario = await Scenario.CreateAsync(provider, AddListener, SeedAsync);
        var (published, reverted) = Sets();
        var before = await scenario.CountRowsAsync();

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var listener = Listener(scope.ServiceProvider);
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            await listener.PublishedAsync(published, null);
            await listener.PublishedAsync(published, reverted);
        }

        Assert.Equal(before, await scenario.CountRowsAsync());
    }

    private static (ChangeSet Published, ChangeSet Reverted) Sets()
    {
        var person = new UserInitiator(Guid.NewGuid(), "Amir");
        var published = ChangeSet.Open(Site.Default.Id, "Undo the sync.", [], person).Value;
        var reverted = ChangeSet.Open(Site.Default.Id, "Sync prices.", ["exports/catalog.csv"], person).Value;
        return (published, reverted);
    }

    private sealed class Scenario(TemporaryDatabase database, ServiceProvider services) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;

        public static async Task<Scenario> CreateAsync(string provider, Action<IServiceCollection> addListener, Func<FluentCmsDbContext, Task> seed)
        {
            var database = await TemporaryDatabase.CreateAsync(provider);
            var services = TestServices.Build(
                database.Configuration,
                collection => collection.AddSingleton<ICurrentInitiator>(new TestInitiator(new UserInitiator(Guid.NewGuid(), "Amir"))),
                addListener);
            await TestServices.MigrateAsync(services);

            await using (var scope = services.CreateAsyncScope())
            {
                await seed(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
            }

            return new Scenario(database, services);
        }

        // The rows a listener in this product can write: jobs it queues and events it appends.
        public async Task<int> CountRowsAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
            return await context.Jobs.CountAsync() + await context.AuditEvents.CountAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}

// Queues a delivery for each subscription that wants the event, so a subscription to both events is seeded.
public sealed class WebhookPublishListenerConformanceTests : ChangeSetEventsConformanceTests
{
    protected override IChangeSetEvents Listener(IServiceProvider services)
    {
        return services.GetServices<IChangeSetEvents>().Single(listener => listener.GetType().Name == "WebhookPublishListener");
    }

    protected override async Task SeedAsync(FluentCmsDbContext context)
    {
        context.WebhookSubscriptions.Add(new WebhookSubscriptionRow
        {
            Id = Guid.CreateVersion7(),
            Url = "https://hooks.example.com/fluentcms",
            Events = [WebhookEvents.ChangeSetPublished, WebhookEvents.ChangeSetReverted],
            ProtectedSecret = "not read by the listener",
            AddedAt = DateTimeOffset.UtcNow,
            AddedById = Guid.NewGuid(),
            AddedByName = "Amir",
        });
        await context.SaveChangesAsync();
    }
}

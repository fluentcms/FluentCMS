using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.ChangeSets;

// A migrated database on one provider with the change set directory acting as whoever Initiator names.
internal sealed class ChangeSetScenario : IAsyncDisposable
{
    public static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");

    public static readonly AgentInitiator Agent = new(Guid.NewGuid(), "example-agent");

    public static readonly NewChangeSet CatalogSync = new("Sync prices from the catalog export.", ["exports/catalog-2026-10-18.csv"]);

    private readonly TemporaryDatabase _database;
    private readonly SwitchableInitiator _initiator;

    private ChangeSetScenario(TemporaryDatabase database, ServiceProvider services, SwitchableInitiator initiator)
    {
        _database = database;
        Services = services;
        _initiator = initiator;
    }

    public ServiceProvider Services { get; }

    public Initiator? Initiator
    {
        get => _initiator.Current;
        set => _initiator.Current = value;
    }

    public static async Task<ChangeSetScenario> CreateAsync(string provider)
    {
        var database = await TemporaryDatabase.CreateAsync(provider);
        var initiator = new SwitchableInitiator { Current = Agent };
        var services = TestServices.Build(
            database.Configuration,
            collection => collection
                .AddSingleton<ICurrentInitiator>(initiator)
                .AddSingleton<TimeProvider>(new TickingClock()));

        await TestServices.MigrateAsync(services);
        return new ChangeSetScenario(database, services, initiator);
    }

    // Each call gets its own scope and DbContext, as separate requests would.
    public async Task<T> RunAsync<T>(Func<IChangeSetDirectory, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>());
    }

    // A content type at version 1, or at version 2 when revised, for operations to point at.
    public async Task<ContentType> ContentTypeAsync(string slug, bool revised = false)
    {
        await using var scope = Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();

        var title = new FieldDefinition("title", "text");
        var type = (await directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([title])))).Value;

        return revised
            ? (await directory.ReviseAsync(slug, 1, new ContentTypeDefinition([title, new FieldDefinition("summary", "text")]))).Value
            : type;
    }

    public async Task<ChangeSet> OpenAsync(params ChangeOperation[] operations)
    {
        var changeSet = (await RunAsync(directory => directory.OpenAsync(CatalogSync))).Value;
        foreach (var operation in operations)
        {
            changeSet = (await RunAsync(directory => directory.AddOperationAsync(changeSet.Id, operation))).Value;
        }

        return changeSet;
    }

    public Task<ChangeSetRow> ChangeSetRowAsync(Guid id)
    {
        return QueryAsync(context => context.ChangeSets.AsNoTracking().SingleAsync(row => row.Id == id));
    }

    public Task<List<ChangeSetRow>> ChangeSetRowsAsync()
    {
        return QueryAsync(context => context.ChangeSets.AsNoTracking().ToListAsync());
    }

    public Task<List<ChangeSetOperationRow>> OperationRowsAsync(Guid changeSetId)
    {
        return QueryAsync(context => context.ChangeSetOperations
            .AsNoTracking()
            .Where(row => row.ChangeSetId == changeSetId)
            .OrderBy(row => row.Position)
            .ToListAsync());
    }

    // Change set events only, in the order they were appended; content type events are left out.
    public async Task<List<AuditEventRow>> ChangeSetEventsAsync(Guid? changeSetId = null)
    {
        var rows = await QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.ChangeSetId != null && (changeSetId == null || row.ChangeSetId == changeSetId))
            .ToListAsync());

        return [.. rows.OrderBy(row => row.Timestamp)];
    }

    public async Task<T> QueryAsync<T>(Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _database.DisposeAsync();
    }

    public static ChangeOperation Update(ContentType type)
    {
        return ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, type.Id, type.Version - 1, type.Version).Value;
    }

    public static ChangeOperation Create(ContentType type)
    {
        return ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value;
    }

    private sealed class SwitchableInitiator : ICurrentInitiator
    {
        public Initiator? Current { get; set; }
    }

    // A millisecond further on every call, so events appended within one call still have an order.
    private sealed class TickingClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero).UtcTicks;

        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(Interlocked.Add(ref _ticks, TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
        }
    }
}

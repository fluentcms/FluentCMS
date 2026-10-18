using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
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
    private readonly AuditFailure _auditFailure;

    private ChangeSetScenario(TemporaryDatabase database, ServiceProvider services, SwitchableInitiator initiator, AuditFailure auditFailure)
    {
        _database = database;
        Services = services;
        _initiator = initiator;
        _auditFailure = auditFailure;
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
        var auditFailure = new AuditFailure();
        var services = TestServices.Build(
            database.Configuration,
            collection => collection
                .AddSingleton<ICurrentInitiator>(initiator)
                .AddSingleton<TimeProvider>(new TickingClock()),
            collection => FailingAuditLog.Wrap(collection, auditFailure));

        await TestServices.MigrateAsync(services);
        return new ChangeSetScenario(database, services, initiator, auditFailure);
    }

    // The given audit append from now on throws, counting from one.
    public void FailOnAppend(int append)
    {
        _auditFailure.FailOnAppend(append);
    }

    // Each call gets its own scope and DbContext, as separate requests would.
    public async Task<T> RunAsync<T>(Func<IChangeSetDirectory, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>());
    }

    // The loser reads while the winner has not committed, so its own checks pass and the database has the last word.
    public async Task<Result<ChangeSet>> RaceAsync(
        Func<IChangeSetDirectory, Task<Result<ChangeSet>>> winner,
        Func<IChangeSetDirectory, Task<Result<ChangeSet>>> loser)
    {
        await using var scope = Services.CreateAsyncScope();
        var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();

        Assert.True((await winner(scope.ServiceProvider.GetRequiredService<IChangeSetDirectory>())).IsSuccess);

        var second = Task.Run(() => RunAsync(loser));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        await work.CommitAsync();
        await work.DisposeAsync();

        return await second;
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

    // The next version of a type, with one more field.
    public async Task<ContentType> ReviseAsync(ContentType type)
    {
        await using var scope = Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>();
        var definition = new ContentTypeDefinition([.. type.Definition.Fields, new FieldDefinition($"note{type.Version + 1}", "text")]);

        return (await directory.ReviseAsync(type.Slug, type.Version, definition)).Value;
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

    // Opened with the operations, submitted and approved, the way routing approves a set without review.
    public async Task<ChangeSet> ApproveAsync(params ChangeOperation[] operations)
    {
        var changeSet = await OpenAsync(operations);
        Assert.True((await RunAsync(directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.ChecksRunning))).IsSuccess);

        return (await RunAsync(directory => directory.TransitionAsync(changeSet.Id, ChangeSetState.Approved))).Value;
    }

    // Approved and published with the operations, so later sets can be based on the versions they publish.
    public async Task<ChangeSet> PublishAsync(params ChangeOperation[] operations)
    {
        var changeSet = await ApproveAsync(operations);
        var published = await RunAsync(directory => directory.PublishAsync(changeSet.Id));
        Assert.True(published.IsSuccess, string.Join("; ", published.Errors.Select(error => error.Message)));

        return published.Value;
    }

    public Task<int?> PublishedVersionAsync(Guid contentTypeId)
    {
        return QueryAsync(context => context.ContentTypes
            .AsNoTracking()
            .Where(row => row.Id == contentTypeId)
            .Select(row => row.PublishedVersion)
            .SingleAsync());
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

    // Events that carry a change set id, in the order they were appended; the content type directory's own events are left out.
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

    public static ChangeOperation Update(ContentType type, int baseVersion, int proposedVersion)
    {
        return ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, type.Id, baseVersion, proposedVersion).Value;
    }

    public static ChangeOperation Delete(ContentType type, int baseVersion)
    {
        return ChangeOperation.Create(OperationKind.Delete, ItemKind.Schema, type.Id, baseVersion, null).Value;
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

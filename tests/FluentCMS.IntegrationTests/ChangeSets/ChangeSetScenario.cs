using System.Security.Cryptography;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Sites;
using FluentCMS.Infrastructure.Agents;
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

    // A second agent with its own stored credential, for what one agent may not do to another's sets.
    public static readonly AgentInitiator OtherAgent = new(Guid.NewGuid(), "other-agent");

    public static readonly NewChangeSet CatalogSync = new("Sync prices from the catalog export.", ["exports/catalog-2026-10-18.csv"]);

    private readonly TemporaryDatabase _database;
    private readonly SwitchableInitiator _initiator;
    private readonly TickingClock _clock;
    private readonly AuditFailure _auditFailure;

    private ChangeSetScenario(
        TemporaryDatabase database,
        ServiceProvider services,
        SwitchableInitiator initiator,
        TickingClock clock,
        AuditFailure auditFailure)
    {
        _database = database;
        Services = services;
        _initiator = initiator;
        _clock = clock;
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
        var clock = new TickingClock();
        var auditFailure = new AuditFailure();
        var services = TestServices.Build(
            database.Configuration,
            collection => collection
                .AddSingleton<ICurrentInitiator>(initiator)
                .AddSingleton<TimeProvider>(clock),
            collection => FailingAuditLog.Wrap(collection, auditFailure));

        await TestServices.MigrateAsync(services);
        var scenario = new ChangeSetScenario(database, services, initiator, clock, auditFailure);
        await scenario.QueryAsync(async context =>
        {
            context.AgentCredentials.AddRange(AgentCredential(Agent), AgentCredential(OtherAgent));
            return await context.SaveChangesAsync();
        });

        return scenario;
    }

    // Stores a credential for another agent with the same scope and caps as Agent.
    public Task<int> AddAgentAsync(AgentInitiator agent)
    {
        return QueryAsync(async context =>
        {
            context.AgentCredentials.Add(AgentCredential(agent));
            return await context.SaveChangesAsync();
        });
    }

    // As a person, who stands in for the checks, routing and the desk, whose moves an agent cannot make (MCP-06).
    public async Task<T> AsPersonAsync<T>(Func<IChangeSetDirectory, Task<T>> action)
    {
        var initiator = Initiator;
        Initiator = Amir;
        try
        {
            return await RunAsync(action);
        }
        finally
        {
            Initiator = initiator;
        }
    }

    public void AdvanceClock(TimeSpan by)
    {
        _clock.Advance(by);
    }

    // Changes Agent's stored credential, such as a cap, its expiry or its revocation.
    public Task<int> UpdateAgentAsync(Action<AgentCredentialRow> change)
    {
        return QueryAsync(async context =>
        {
            change(await context.AgentCredentials.SingleAsync(row => row.Id == Agent.CredentialId));
            return await context.SaveChangesAsync();
        });
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

    // A content type at version 1, or at version 2 when revised, for operations to point at. A person writes it in the content
    // model's own set, which no test publishes and the change set reads below leave out.
    public Task<ContentType> ContentTypeAsync(string slug, bool revised = false)
    {
        return AsSchemaAuthorAsync(async directory =>
        {
            var title = new FieldDefinition("title", "text");
            var type = (await directory.CreateAsync(new NewContentType(slug, slug, new ContentTypeDefinition([title])), TestChangeSetTargets.ContentModel)).Value;

            return revised
                ? (await directory.ReviseAsync(slug, 1, new ContentTypeDefinition([title, new FieldDefinition("summary", "text")]), TestChangeSetTargets.ContentModel)).Value
                : type;
        });
    }

    // The next version of a type, with one more field.
    public Task<ContentType> ReviseAsync(ContentType type)
    {
        var definition = new ContentTypeDefinition([.. type.Definition.Fields, new FieldDefinition($"note{type.Version + 1}", "text")]);

        return AsSchemaAuthorAsync(async directory =>
            (await directory.ReviseAsync(type.Slug, type.Version, definition, TestChangeSetTargets.ContentModel)).Value);
    }

    // The content type directory as whoever Initiator names, in a scope of its own.
    public async Task<T> RunSchemaAsync<T>(Func<IContentTypeDirectory, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>());
    }

    private async Task<T> AsSchemaAuthorAsync<T>(Func<IContentTypeDirectory, Task<T>> action)
    {
        var initiator = Initiator;
        Initiator = Amir;
        try
        {
            await using var scope = Services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<IContentTypeDirectory>());
        }
        finally
        {
            Initiator = initiator;
        }
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
        return await ApproveAsync((await OpenAsync(operations)).Id);
    }

    public async Task<ChangeSet> ApproveAsync(Guid changeSetId)
    {
        Assert.True((await RunAsync(directory => directory.TransitionAsync(changeSetId, ChangeSetState.ChecksRunning))).IsSuccess);

        return (await AsPersonAsync(directory => directory.TransitionAsync(changeSetId, ChangeSetState.Approved))).Value;
    }

    // Approved and published with the operations, so later sets can be based on the versions they publish.
    public async Task<ChangeSet> PublishAsync(params ChangeOperation[] operations)
    {
        return await PublishAsync((await OpenAsync(operations)).Id);
    }

    public async Task<ChangeSet> PublishAsync(Guid changeSetId)
    {
        await ApproveAsync(changeSetId);
        var published = await AsPersonAsync(directory => directory.PublishAsync(changeSetId));
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
        return QueryAsync(context => context.ChangeSets
            .AsNoTracking()
            .Where(row => row.SessionId != TestChangeSetTargets.ContentModelSession)
            .ToListAsync());
    }

    public Task<List<ChangeSetOperationRow>> OperationRowsAsync(Guid changeSetId)
    {
        return QueryAsync(context => context.ChangeSetOperations
            .AsNoTracking()
            .Where(row => row.ChangeSetId == changeSetId)
            .OrderBy(row => row.Position)
            .ToListAsync());
    }

    // Events that carry a change set id, in the order they were appended; those of the content model's set are left out.
    public async Task<List<AuditEventRow>> ChangeSetEventsAsync(Guid? changeSetId = null)
    {
        var rows = await QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.ChangeSetId != null && (changeSetId == null || row.ChangeSetId == changeSetId))
            .Where(row => !context.ChangeSets.Any(set => set.Id == row.ChangeSetId && set.SessionId == TestChangeSetTargets.ContentModelSession))
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

    // The stored credential behind an agent, scoped to the site with room to spare, so only a test that lowers a cap meets one.
    private static AgentCredentialRow AgentCredential(AgentInitiator agent)
    {
        return new AgentCredentialRow
        {
            Id = agent.CredentialId,
            DisplayName = agent.DisplayName,
            KeyHash = RandomNumberGenerator.GetBytes(32),
            IssuedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2027, 10, 1, 0, 0, 0, TimeSpan.Zero),
            Sites = [Site.Default.Id],
            Operations = [AgentOperation.Read, AgentOperation.Create, AgentOperation.Update, AgentOperation.Delete],
            CallsPerPeriod = 1000,
            CallPeriod = TimeSpan.FromHours(1),
            LlmSpendPeriod = TimeSpan.FromDays(1),
            ItemsPerChangeSet = 1000,
            ChangeSetsPerDay = 1000,
        };
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

        public void Advance(TimeSpan by)
        {
            Interlocked.Add(ref _ticks, by.Ticks);
        }
    }
}

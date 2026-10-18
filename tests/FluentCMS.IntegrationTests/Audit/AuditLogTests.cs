using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Infrastructure.Audit;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Audit;

public sealed class AuditLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");
    private static readonly AgentInitiator CatalogSync = new(Guid.NewGuid(), "catalog-sync");

    [Fact]
    public async Task An_event_appended_inside_a_unit_of_work_is_persisted_with_the_current_user_and_every_field()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Amir);
        var subjectId = Guid.NewGuid();
        var changeSetId = Guid.NewGuid();
        var previousVersionId = Guid.NewGuid();
        AuditEvent appended;

        await using (var scope = services.CreateAsyncScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            appended = await scope.ServiceProvider.GetRequiredService<IAuditLog>().AppendAsync(new NewAuditEvent("entry.updated")
            {
                SubjectType = "entry",
                SubjectId = subjectId,
                Model = "example-model",
                PromptHash = "sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                Sources = ["https://example.com/prices", "Dana, fact owner"],
                ChangeSetId = changeSetId,
                PreviousVersionId = previousVersionId,
            });
            await work.CommitAsync();
        }

        Assert.Same(Amir, appended.Initiator);
        Assert.Equal(Now, appended.Timestamp);

        var row = Assert.Single(await RowsAsync(services));
        Assert.Equal(appended.Id, row.Id);
        Assert.Equal(Now, row.Timestamp);
        Assert.Equal(InitiatorKinds.User, row.InitiatorKind);
        Assert.Equal(Amir.UserId, row.InitiatorId);
        Assert.Equal("Amir", row.InitiatorName);
        Assert.Null(row.AgentCredentialId);
        Assert.Equal("entry.updated", row.Operation);
        Assert.Equal("entry", row.SubjectType);
        Assert.Equal(subjectId, row.SubjectId);
        Assert.Equal("example-model", row.Model);
        Assert.StartsWith("sha256:", row.PromptHash);
        Assert.Equal(["https://example.com/prices", "Dana, fact owner"], row.Sources);
        Assert.Equal(changeSetId, row.ChangeSetId);
        Assert.Equal(previousVersionId, row.PreviousVersionId);
    }

    [Fact]
    public async Task An_agent_event_records_the_credential_it_acted_under()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, CatalogSync);

        await using (var scope = services.CreateAsyncScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync();
            await scope.ServiceProvider.GetRequiredService<IAuditLog>().AppendAsync(new NewAuditEvent("entry.created"));
            await work.CommitAsync();
        }

        var row = Assert.Single(await RowsAsync(services));
        Assert.Equal(InitiatorKinds.Agent, row.InitiatorKind);
        Assert.Equal(CatalogSync.CredentialId, row.InitiatorId);
        Assert.Equal(CatalogSync.CredentialId, row.AgentCredentialId);
        Assert.Equal("catalog-sync", row.InitiatorName);
        Assert.Empty(row.Sources);
    }

    [Fact]
    public async Task A_unit_of_work_disposed_without_commit_leaves_no_event_and_no_change_behind()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Amir);
        await using var scope = services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        // Identity saves the user on its own inside the scope; the rollback still takes it away together with the event (AUD-N1).
        await using (await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync())
        {
            var created = await directory.CreateAsync(new NewUser("owner@example.com", "Owner", "Str0ng!Passw0rd"));
            Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));

            await scope.ServiceProvider.GetRequiredService<IAuditLog>().AppendAsync(
                new NewAuditEvent("user.created") { SubjectType = "user", SubjectId = created.Value });
        }

        Assert.Empty(await RowsAsync(services));
        Assert.Empty(await directory.ListAsync());
    }

    [Fact]
    public async Task Appending_outside_a_unit_of_work_is_rejected()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Amir);
        await using var scope = services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => log.AppendAsync(new NewAuditEvent("entry.created")));

        Assert.Contains("unit of work", exception.Message);
        Assert.Empty(await RowsAsync(services));
    }

    [Fact]
    public async Task Appending_with_no_known_initiator_is_rejected()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, initiator: null);
        await using var scope = services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        await using (var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync())
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => log.AppendAsync(new NewAuditEvent("job.ran")));
            Assert.Contains("initiator", exception.Message);
            await work.CommitAsync();
        }

        Assert.Empty(await RowsAsync(services));
    }

    [Fact]
    public async Task Inner_scopes_flush_into_the_outer_transaction_and_only_the_outer_commit_makes_them_visible()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Amir);
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var log = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        await using (var outer = await unitOfWork.BeginAsync())
        {
            await log.AppendAsync(new NewAuditEvent("outer.started"));
            await using (var inner = await unitOfWork.BeginAsync())
            {
                await log.AppendAsync(new NewAuditEvent("inner.done"));
                await inner.CommitAsync();
            }

            await outer.CommitAsync();
        }

        Assert.Equal(["inner.done", "outer.started"], (await RowsAsync(services)).Select(row => row.Operation).Order());

        await using (await unitOfWork.BeginAsync())
        {
            await using var inner = await unitOfWork.BeginAsync();
            await log.AppendAsync(new NewAuditEvent("inner.committed_but_outer_abandoned"));
            await inner.CommitAsync();
        }

        Assert.Equal(2, (await RowsAsync(services)).Count);
    }

    [Fact]
    public async Task An_inner_scope_disposed_without_commit_fails_the_outer_commit()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Amir);
        await using var scope = services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var log = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        await using (var outer = await unitOfWork.BeginAsync())
        {
            await log.AppendAsync(new NewAuditEvent("outer.started"));
            await using (await unitOfWork.BeginAsync())
            {
                await log.AppendAsync(new NewAuditEvent("inner.abandoned"));
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => outer.CommitAsync());
        }

        Assert.Empty(await RowsAsync(services));

        // The next unit of work on the same context starts clean.
        await using (var work = await unitOfWork.BeginAsync())
        {
            await log.AppendAsync(new NewAuditEvent("next.done"));
            await work.CommitAsync();
        }

        Assert.Equal("next.done", Assert.Single(await RowsAsync(services)).Operation);
    }

    private static async Task<ServiceProvider> BuildAsync(TemporarySqliteDatabase database, Initiator? initiator)
    {
        var services = TestServices.Build(database.Configuration, collection =>
        {
            collection.AddSingleton<TimeProvider>(new FixedClock(Now));
            collection.AddScoped<ICurrentInitiator>(_ => new TestInitiator(initiator));
        });

        await TestServices.MigrateAsync(services);
        return services;
    }

    private static async Task<List<AuditEventRow>> RowsAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents.ToListAsync();
    }
}

using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Data.Tests.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Data.Tests.Storage;

// The dead-letter lists page newest first over rows that are kept for weeks, so each walks an index of its own rows in that
// order rather than sorting every job or every outbox row.
public sealed class DeadLetterPlanTests : IDisposable
{
    private readonly JobFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [SqliteFact("SQLite's query plans")]
    public void TheListOfDeadLetteredJobsWalksTheIndexOnStatusAndCompletion()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var plan = QueryPlanTests.Plan(context.Set<Job>().AsNoTracking()
            .Where(job => job.Status == JobStatus.DeadLettered)
            .OrderByDescending(job => job.CompletedAt)
            .Skip(50)
            .Take(50));

        plan.ShouldContain("ix_cms_job_status_completed");
        plan.ShouldNotContain("TEMP B-TREE");
    }

    [SqliteFact("SQLite's query plans")]
    public void TheListOfFailedDeliveriesWalksTheFailedIndex()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var plan = QueryPlanTests.Plan(context.Set<OutboxMessage>().AsNoTracking()
            .Where(message => message.Status == OutboxStatus.Failed)
            .OrderByDescending(message => message.Sequence)
            .Skip(50)
            .Take(50));

        plan.ShouldContain("ix_cms_outbox_failed");
        plan.ShouldNotContain("TEMP B-TREE");
    }
}

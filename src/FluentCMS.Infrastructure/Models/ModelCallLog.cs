using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Models;

// Time windows are queried in SQL because EF Core will not compare a DateTimeOffset on SQLite, which stores it as text; stored
// timestamps are all UTC, so the text compares in time order on both providers, as in AgentActivity.
internal sealed class ModelCallLog(
    IServiceScopeFactory scopes,
    FluentCmsDbContext context,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    IOptions<ModelOptions> options,
    TimeProvider clock) : IModelCallLog
{
    // A purge clears at most this many calls, so one run stays one short transaction; the next run takes the rest.
    public const int PurgeBatch = 1000;

    private const string SubjectType = "model_call";

    public async Task AppendAsync(ModelCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);

        await using var scope = scopes.CreateAsyncScope();
        var own = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        own.ModelCalls.Add(ModelCallRow.From(call));
        await own.SaveChangesAsync(cancellationToken);
    }

    public async Task<decimal> SpendAsync(Guid agentCredentialId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        // Summed here: SQLite stores decimals as text and cannot add them up.
        var costs = await context.ModelCalls
            .FromSql($"""
                SELECT * FROM "ModelCalls" WHERE "AgentCredentialId" = {agentCredentialId} AND "Timestamp" >= {since.ToUniversalTime()}
                """)
            .Select(call => call.Cost)
            .ToListAsync(cancellationToken);

        return costs.Sum();
    }

    public async Task<IReadOnlyList<ModelCall>> FindByPromptHashAsync(string promptHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(promptHash);

        var rows = await context.ModelCalls.AsNoTracking().Where(call => call.PromptHash == promptHash).OrderBy(call => call.Id).ToListAsync(cancellationToken);
        return rows.Select(row => row.ToCall()).ToList();
    }

    public async Task<Result<int>> PurgeAsync(CancellationToken cancellationToken = default)
    {
        // Purging removes what the audit log points at, which no agent decides (AUD-N2).
        if (currentInitiator.Current is not UserInitiator)
        {
            return Result<int>.Failure(ModelErrorCodes.PeopleOnly, "Only a signed-in person can purge stored prompts; an agent cannot.");
        }

        var now = clock.GetUtcNow();
        var cutoff = (now - options.Value.PromptRetention).ToUniversalTime();
        var expired = await context.ModelCalls
            .FromSql($"""SELECT * FROM "ModelCalls" WHERE "Timestamp" < {cutoff} AND "PurgedAt" IS NULL""")
            .AsNoTracking()
            .OrderBy(call => call.Id)
            .Take(PurgeBatch)
            .Select(call => new { call.Id, call.PromptHash, Model = call.ModelVersion ?? call.Model })
            .ToListAsync(cancellationToken);

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var purged = 0;
        foreach (var call in expired)
        {
            // Only a call nobody purged in the meantime is cleared and logged, so two purges at once log each call once.
            var cleared = await context.ModelCalls
                .Where(row => row.Id == call.Id && row.PurgedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.Prompt, (string?)null)
                        .SetProperty(row => row.Response, (string?)null)
                        .SetProperty(row => row.PurgedAt, now),
                    cancellationToken);
            if (cleared == 0)
            {
                continue;
            }

            await auditLog.AppendAsync(
                new NewAuditEvent("model_call.purged") { SubjectType = SubjectType, SubjectId = call.Id, Model = call.Model, PromptHash = call.PromptHash },
                cancellationToken);
            purged++;
        }

        await scope.CommitAsync(cancellationToken);
        return Result<int>.Success(purged);
    }
}

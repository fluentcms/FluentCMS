using Cronos;
using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

internal sealed class RecurringJobs(IServiceScopeFactory scopes, JobRunnerOptions options, TimeProvider time, ILogger<RecurringJobs> logger)
{
    private const int NameMaxLength = 128;

    public async Task AddAsync(string name, string cron, string type, string payload, string queue, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, NameMaxLength, nameof(name));

        var next = NextOccurrence(ParseCron(cron), time.GetUtcNow()) ?? throw new ArgumentException($"{JobErrorCodes.InvalidCron}: the cron expression '{cron}' never occurs.", nameof(cron));

        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        if (await UpdateAsync(context, name, cron, type, payload, queue, next, cancellationToken))
        {
            return;
        }

        context.Add(new RecurringJob { Name = name, Cron = cron, Type = type, Payload = payload, Queue = queue, NextRunAt = next, Enabled = true });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another instance added it at the same startup, so what is left is an update.
            context.ChangeTracker.Clear();
            if (!await UpdateAsync(context, name, cron, type, payload, queue, next, cancellationToken))
            {
                throw;
            }
        }
    }

    public async Task FireDueAsync(CancellationToken cancellationToken = default)
    {
        List<RecurringJob> due;
        var now = time.GetUtcNow();

        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<RecurringJob>().AsNoTracking()
                .Where(job => job.Enabled && job.NextRunAt <= now)
                .ToListAsync(cancellationToken);
        }

        foreach (var job in due)
        {
            if (Next(job, now) is not { } next)
            {
                continue;
            }

            try
            {
                await FireAsync(job, next, now, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                JobLog.RecurringFireFailed(logger, exception, job.Name);
            }
        }
    }

    // Only the instance that moves next_run_at on enqueues the job, so an occurrence fires once however many try.
    private async Task FireAsync(RecurringJob due, DateTimeOffset next, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var moved = await context.Set<RecurringJob>()
            .Where(job => job.Name == due.Name && job.Enabled && job.NextRunAt == due.NextRunAt)
            .ExecuteUpdateAsync(job => job.SetProperty(row => row.NextRunAt, next), cancellationToken);

        if (moved == 0)
        {
            return;
        }

        context.Add(new Job
        {
            Type = due.Type,
            Payload = due.Payload,
            Queue = due.Queue,
            RunAt = due.NextRunAt,
            MaxAttempts = options.DefaultMaxAttempts,
            CreatedAt = now,
            Origin = Origin.Job,
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // A row can hold what AddAsync would refuse, if it was edited by hand.
    private DateTimeOffset? Next(RecurringJob job, DateTimeOffset now)
    {
        try
        {
            if (NextOccurrence(ParseCron(job.Cron), now) is { } next)
            {
                return next;
            }

            JobLog.RecurringCronInvalid(logger, JobErrorCodes.InvalidCron, job.Name, "it never occurs");
        }
        catch (ArgumentException exception)
        {
            JobLog.RecurringCronInvalid(logger, JobErrorCodes.InvalidCron, job.Name, exception.Message);
        }

        return null;
    }

    private static async Task<bool> UpdateAsync(SystemDbContext context, string name, string cron, string type, string payload, string queue, DateTimeOffset next, CancellationToken cancellationToken)
    {
        var rows = context.Set<RecurringJob>().Where(job => job.Name == name);

        // Only a changed cron moves the next run, so that a restart does not skip an occurrence.
        await rows.Where(job => job.Cron != cron)
            .ExecuteUpdateAsync(job => job.SetProperty(row => row.NextRunAt, next), cancellationToken);

        var updated = await rows.ExecuteUpdateAsync(
            job => job
                .SetProperty(row => row.Cron, cron)
                .SetProperty(row => row.Type, type)
                .SetProperty(row => row.Payload, payload)
                .SetProperty(row => row.Queue, queue),
            cancellationToken);

        return updated == 1;
    }

    private static DateTimeOffset? NextOccurrence(CronExpression expression, DateTimeOffset after) =>
        expression.GetNextOccurrence(after, TimeZoneInfo.Utc);

    private static CronExpression ParseCron(string cron)
    {
        ArgumentException.ThrowIfNullOrEmpty(cron);

        try
        {
            return CronExpression.Parse(cron);
        }
        catch (CronFormatException exception)
        {
            throw new ArgumentException($"{JobErrorCodes.InvalidCron}: the cron expression '{cron}' is not valid: {exception.Message}", nameof(cron), exception);
        }
    }
}

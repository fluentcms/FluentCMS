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

    // Fires each due job once, however many instances try: only the one that moves next_run_at on enqueues it. Runs
    // missed while nothing was up collapse into one.
    public async Task FireDueAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        var now = time.GetUtcNow();
        var due = await context.Set<RecurringJob>().AsNoTracking()
            .Where(job => job.Enabled && job.NextRunAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var job in due)
        {
            if (Next(job, now) is { } next)
            {
                await FireAsync(context, job, next, now, cancellationToken);
            }
        }
    }

    private async Task FireAsync(SystemDbContext context, RecurringJob due, DateTimeOffset next, DateTimeOffset now, CancellationToken cancellationToken)
    {
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
        context.ChangeTracker.Clear();
    }

    // A row can hold what AddAsync would refuse, if it was edited by hand, and that must not stop the other schedules.
    private DateTimeOffset? Next(RecurringJob job, DateTimeOffset now)
    {
        try
        {
            if (NextOccurrence(ParseCron(job.Cron), now) is { } next)
            {
                return next;
            }

            JobLog.RecurringCronInvalid(logger, job.Name, "it never occurs");
        }
        catch (ArgumentException exception)
        {
            JobLog.RecurringCronInvalid(logger, job.Name, exception.Message);
        }

        return null;
    }

    private static async Task<bool> UpdateAsync(SystemDbContext context, string name, string cron, string type, string payload, string queue, DateTimeOffset next, CancellationToken cancellationToken)
    {
        var rows = context.Set<RecurringJob>().Where(job => job.Name == name);

        // A changed cron starts from now; the same one keeps its next run, so a restart does not skip an occurrence.
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

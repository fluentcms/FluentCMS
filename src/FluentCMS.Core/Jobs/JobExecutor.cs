using System.Diagnostics;
using FluentCMS.Content;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Core.Telemetry;
using FluentCMS.Jobs;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

// A job of a plugin disabled in the job's site completes without running; one of no site is the instance's and always runs.
// When which plugins are disabled cannot be read, the job is put back without using an attempt, as the outbox does with a
// delivery, and waits one poll so that the worker does not claim it again at once.
internal sealed class JobExecutor(
    JobStore store,
    JobRegistry registry,
    PluginState plugins,
    IServiceScopeFactory scopes,
    IOperationContextAccessor operation,
    JobRunnerOptions options,
    SchemaModel schema,
    TimeProvider time,
    ILogger<JobExecutor> logger)
{
    private static readonly TimeSpan FirstSettleRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LastSettleRetry = TimeSpan.FromSeconds(30);

    private readonly IReadOnlySet<string> _schemaNames = JobErrorText.NamesOf(schema);

    private enum Invocation
    {
        Ran,
        Skipped,
        Deferred,
    }

    // False when the queue has nothing due.
    public async Task<bool> RunNextAsync(
        string queue,
        string owner,
        CancellationToken stopClaiming = default,
        CancellationToken shutdown = default,
        CancellationToken abandon = default)
    {
        if (await store.ClaimAsync(queue, owner, stopClaiming) is not { } job)
        {
            return false;
        }

        await RunAsync(job, shutdown, abandon);
        return true;
    }

    public async Task RunAsync(ClaimedJob job, CancellationToken shutdown = default, CancellationToken abandon = default)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        using var stopHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(abandon);

        var heartbeat = HeartbeatAsync(job, attempt, stopHeartbeat.Token);
        try
        {
            var limit = AttemptTimeoutOf(job);
            using var deadline = limit is { } span ? new CancellationTokenSource(span, time) : null;
            var timedOut = false;

            // Renewal stops first, so that a job that ignores its token also loses its lease and is taken over.
            using var expiry = deadline?.Token.Register(() =>
            {
                timedOut = true;
                stopHeartbeat.Cancel();
                attempt.Cancel();
            });

            await ExecuteAndSettleAsync(job, () => timedOut ? limit : null, shutdown, attempt.Token, abandon);
        }
        finally
        {
            await stopHeartbeat.CancelAsync();
            await heartbeat;
        }
    }

    private TimeSpan? AttemptTimeoutOf(ClaimedJob job)
    {
        var limit = registry.Find(job.Type)?.AttemptTimeout ?? options.AttemptTimeout;
        return limit == Timeout.InfiniteTimeSpan ? null : limit;
    }

    private async Task ExecuteAndSettleAsync(ClaimedJob job, Func<TimeSpan?> timedOut, CancellationToken shutdown, CancellationToken attempt, CancellationToken abandon)
    {
        var started = Stopwatch.GetTimestamp();
        Func<Task<bool>> settle;
        try
        {
            var invocation = await InvokeAsync(job, attempt);
            if (invocation == Invocation.Deferred)
            {
                Record(job, started, "released");
                var runAt = time.GetUtcNow() + options.PollInterval;
                settle = () => store.ReleaseAsync(job, runAt);
            }
            else
            {
                Record(job, started, invocation == Invocation.Ran ? "succeeded" : "skipped");
                settle = () => store.SucceedAsync(job);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Record(job, started, "released");
            JobLog.Released(logger, job.Id, job.Type, job.Attempt);
            settle = () => store.ReleaseAsync(job);
        }
        catch (OperationCanceledException) when (timedOut() is { } limit)
        {
            Record(job, started, "failed");
            var error = JobErrorText.ForStorage(TimedOut(limit), job.Payload, _schemaNames);
            JobLog.AttemptFailed(logger, job.Id, job.Type, job.Attempt, error);
            settle = () => store.FailAsync(job, error);
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            Record(job, started, "lease-lost");
            JobLog.LeaseLost(logger, job.Id, job.Type, job.Attempt);
            return;
        }
        catch (Exception exception)
        {
            Record(job, started, "failed");
            JobLog.AttemptFailed(logger, job.Id, job.Type, job.Attempt, JobErrorText.ForLog(exception, job.Payload, _schemaNames));
            var error = JobErrorText.ForStorage(exception, job.Payload, _schemaNames);
            settle = () => store.FailAsync(job, error);
        }

        await SettleWithRetriesAsync(job, settle, abandon);
    }

    private static void Record(ClaimedJob job, long started, string outcome) =>
        FluentCmsTelemetry.JobLatency.Record(
            Stopwatch.GetElapsedTime(started).TotalSeconds,
            new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Operation, job.Type),
            new KeyValuePair<string, object?>(FluentCmsTelemetry.Tags.Outcome, outcome));

    // A result not written means the job runs again, or is dead-lettered as if it never finished, so a brief outage is
    // waited out. Retrying past the real end of the lease is safe, because a write matches nothing once another worker
    // has the job. It gives up after one lease, in case the failure is not an outage.
    private async Task SettleWithRetriesAsync(ClaimedJob job, Func<Task<bool>> settle, CancellationToken abandon)
    {
        var giveUpAt = time.GetUtcNow() + options.LeaseDuration;
        var delay = FirstSettleRetry;

        while (true)
        {
            try
            {
                if (!await settle())
                {
                    JobLog.LeaseLost(logger, job.Id, job.Type, job.Attempt);
                }

                return;
            }
            catch (Exception exception) when (time.GetUtcNow() + delay < giveUpAt)
            {
                JobLog.SettleFailed(logger, exception, job.Id);
            }

            await Task.Delay(delay, time, abandon);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, LastSettleRetry.Ticks));
        }
    }

    private async Task<Invocation> InvokeAsync(ClaimedJob job, CancellationToken attempt)
    {
        var registration = registry.Find(job.Type)
            ?? throw new JobInvocationException($"{JobErrorCodes.UnknownJobType}: no job is registered for the type '{job.Type}'.");

        string? disabled;
        try
        {
            disabled = await plugins.DisabledJobAsync(job.Type, job.SiteId, attempt);
        }
        catch (Exception exception) when (!attempt.IsCancellationRequested)
        {
            PluginLog.JobDeferred(logger, exception, job.Id, job.Type);
            return Invocation.Deferred;
        }

        if (disabled is not null)
        {
            PluginLog.JobSkipped(logger, job.Id, job.Type, disabled, job.SiteId!.Value);
            return Invocation.Skipped;
        }

        var payload = ReadPayload(registration, job);

        await using var scope = scopes.CreateAsyncScope();
        var context = new JobContext(job.Id, job.Type, job.Queue, job.Attempt, job.MaxAttempts, await OperationOfAsync(job, payload, scope.ServiceProvider), attempt);

        var previous = operation.Current;
        operation.Current = context.Operation;
        using var span = FluentCmsTelemetry.Start("job", job.Type, null, context.Operation);
        span?.SetTag(FluentCmsTelemetry.Tags.Queue, job.Queue);
        try
        {
            await registration.Invoke(scope.ServiceProvider, payload, context);
            return Invocation.Ran;
        }
        catch (Exception)
        {
            span?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            operation.Current = previous;
        }
    }

    private static JobInvocationException TimedOut(TimeSpan limit) =>
        new($"{JobErrorCodes.AttemptTimedOut}: the attempt ran longer than {limit}.");

    // Keeps nothing of the reader's exception: a converter can quote any value in it, and a path can name a dictionary key.
    private static object ReadPayload(JobRegistration registration, ClaimedJob job)
    {
        try
        {
            return registration.Read(job.Payload)
                ?? throw new JobInvocationException($"{JobErrorCodes.BadPayload}: the payload of '{job.Type}' is null.");
        }
        catch (Exception exception) when (exception is not JobInvocationException)
        {
            throw new JobInvocationException(
                $"{JobErrorCodes.BadPayload}: the payload of '{job.Type}' could not be read as {registration.Payload.Name} ({exception.GetType().Name}).");
        }
    }

    private async Task<OperationContext> OperationOfAsync(ClaimedJob job, object payload, IServiceProvider services)
    {
        var site = job.SiteId ?? DefaultSite.Id;
        var locale = await services.GetRequiredService<SystemDbContext>().Set<Site>()
            .Where(row => row.Id == site)
            .Select(row => row.DefaultLocale)
            .FirstOrDefaultAsync();

        return new OperationContext(Principal.System, Origin.Job, new SiteId(site), locale ?? DefaultSite.Locale, ClientId: null, job.Id.ToString(), time.GetUtcNow())
        {
            Actor = ActorOf(job, payload),
        };
    }

    // Built from the id alone, so that nothing in a payload can bring claims. It never replaces Principal.System.
    private static Principal? ActorOf(ClaimedJob job, object payload)
    {
        if (payload is not IJobActorPayload { Actor: { } actor })
        {
            return null;
        }

        if (actor.Id == Guid.Empty)
        {
            throw new JobInvocationException($"{JobErrorCodes.BadActor}: the payload of '{job.Type}' names an actor with an empty id.");
        }

        try
        {
            return new Principal(actor.Kind, actor.Id, actor.Name);
        }
        catch (ArgumentException)
        {
            throw new JobInvocationException($"{JobErrorCodes.BadActor}: the payload of '{job.Type}' names an actor of kind {actor.Kind}, which is not an identity a job can act for.");
        }
    }

    private async Task HeartbeatAsync(ClaimedJob job, CancellationTokenSource attempt, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(options.HeartbeatInterval, time);
        var leaseUntil = job.LeaseUntil;
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    if (await store.RenewLeaseAsync(job) is not { } renewed)
                    {
                        await attempt.CancelAsync();
                        return;
                    }

                    leaseUntil = renewed;
                }
                catch (Exception exception) when (!stop.IsCancellationRequested)
                {
                    JobLog.HeartbeatFailed(logger, exception, job.Id);

                    // With no renewal before the lease ends, another worker can take the job, so stop first.
                    if (time.GetUtcNow() + options.HeartbeatInterval >= leaseUntil)
                    {
                        await attempt.CancelAsync();
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }
}

// The job could not start. The message is what last_error stores, and never holds a value of the payload.
internal sealed class JobInvocationException(string message) : Exception(message);

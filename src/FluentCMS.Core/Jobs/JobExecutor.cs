using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Jobs;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Jobs;

// Runs one attempt: claims it, holds the lease while the job runs and writes the outcome. Whatever the job does stays
// in here as a failed attempt.
internal sealed class JobExecutor(
    JobStore store,
    JobRegistry registry,
    IServiceScopeFactory scopes,
    IOperationContextAccessor operation,
    JobRunnerOptions options,
    TimeProvider time,
    ILogger<JobExecutor> logger)
{
    // False when the queue has nothing due.
    public async Task<bool> RunNextAsync(string queue, string owner, CancellationToken shutdown = default, CancellationToken abandon = default)
    {
        if (await store.ClaimAsync(queue, owner) is not { } job)
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
            await ExecuteAndSettleAsync(job, shutdown, attempt.Token);
        }
        finally
        {
            await stopHeartbeat.CancelAsync();
            await heartbeat;
        }
    }

    private async Task ExecuteAndSettleAsync(ClaimedJob job, CancellationToken shutdown, CancellationToken attempt)
    {
        try
        {
            await InvokeAsync(job, attempt);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            if (await store.ReleaseAsync(job))
            {
                JobLog.Released(logger, job.Id, job.Type, job.Attempt);
            }

            return;
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            JobLog.LeaseLost(logger, job.Id, job.Type, job.Attempt);
            return;
        }
        catch (Exception exception)
        {
            JobLog.AttemptFailed(logger, job.Id, job.Type, job.Attempt, JobErrorText.ForLog(exception, job.Payload));
            await SettleFailureAsync(job, JobErrorText.ForStorage(exception, job.Payload));
            return;
        }

        if (!await store.SucceedAsync(job))
        {
            JobLog.LeaseLost(logger, job.Id, job.Type, job.Attempt);
        }
    }

    private async Task SettleFailureAsync(ClaimedJob job, string error)
    {
        if (!await store.FailAsync(job, error))
        {
            JobLog.LeaseLost(logger, job.Id, job.Type, job.Attempt);
        }
    }

    private async Task InvokeAsync(ClaimedJob job, CancellationToken attempt)
    {
        var registration = registry.Find(job.Type)
            ?? throw new JobInvocationException($"{JobErrorCodes.UnknownJobType}: no job is registered for the type '{job.Type}'.");

        var payload = ReadPayload(registration, job);

        await using var scope = scopes.CreateAsyncScope();
        var context = new JobContext(job.Id, job.Type, job.Queue, job.Attempt, job.MaxAttempts, await OperationOfAsync(job, payload, scope.ServiceProvider), attempt);

        var previous = operation.Current;
        operation.Current = context.Operation;
        try
        {
            await registration.Invoke(scope.ServiceProvider, payload, context);
        }
        finally
        {
            operation.Current = previous;
        }
    }

    // No part of the reader's exception is kept, because it can quote the payload: a converter can put anything in it.
    private static object ReadPayload(JobRegistration registration, ClaimedJob job)
    {
        try
        {
            return registration.Read(job.Payload)
                ?? throw new JobInvocationException($"{JobErrorCodes.BadPayload}: the payload of '{job.Type}' is null.");
        }
        catch (Exception exception) when (exception is not JobInvocationException)
        {
            var path = exception is JsonException { Path: { } at } ? $" at {at}" : "";
            throw new JobInvocationException(
                $"{JobErrorCodes.BadPayload}: the payload of '{job.Type}' could not be read as {registration.Payload.FullName}{path} ({exception.GetType().Name}).");
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

    // The principal is built here from the id alone, so that nothing in a payload can bring claims, and it never
    // replaces Principal.System: it is what audit records, and authorization does not read it.
    private static Principal? ActorOf(ClaimedJob job, object payload)
    {
        if (payload is not IJobActorPayload { Actor: { } actor })
        {
            return null;
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
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    if (!await store.RenewLeaseAsync(job))
                    {
                        // Another worker has it, so this one must stop.
                        await attempt.CancelAsync();
                        return;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    JobLog.HeartbeatFailed(logger, exception, job.Id);
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

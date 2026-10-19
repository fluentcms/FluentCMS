using System.Collections.Frozen;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Jobs;

// The job handlers that run as an engine, each with the one engine it runs as (ADR-0012). Only Core lists them, so no other
// handler, a plugin's included (EXT-03), borrows an engine's permissions by naming it in RunAs.
public sealed class EngineJobs
{
    private readonly FrozenDictionary<Type, SystemInitiator> _engines;

    internal EngineJobs(IReadOnlyDictionary<Type, SystemInitiator> engines)
    {
        _engines = engines.ToFrozenDictionary();
    }

    // The engine a job runs as, null to run it as whoever enqueued it, or a failure, so the worker fails the job instead of running
    // it as anyone: when its handler names an engine it is not listed for, or when an engine enqueued it for a handler not listed for
    // that engine, as code running inside an engine's scope might, to pass the engine on.
    public Result<SystemInitiator?> RunAs(IJobHandler handler, Initiator? enqueuedBy)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var listed = _engines.GetValueOrDefault(handler.GetType());
        if (handler.RunAs is { } claimed)
        {
            return listed == claimed
                ? Result<SystemInitiator?>.Success(claimed)
                : Result<SystemInitiator?>.Failure(
                    JobErrorCodes.EngineNotAllowed,
                    $"The handler of '{handler.Type}' jobs claims to run as the '{claimed.Component}' engine, which only Core's own handlers may.");
        }

        return enqueuedBy is SystemInitiator engine && listed != engine
            ? Result<SystemInitiator?>.Failure(
                JobErrorCodes.EngineNotAllowed,
                $"The '{engine.Component}' engine enqueued a '{handler.Type}' job, and an engine's jobs run only through the handlers Core lists for it.")
            : Result<SystemInitiator?>.Success(null);
    }
}

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

    // The engine a handler's jobs run as, null to run them as whoever enqueued them, or a failure when it names an engine it is not
    // listed for, so the worker fails the job instead of running it as anyone.
    public Result<SystemInitiator?> RunAs(IJobHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (handler.RunAs is not { } claimed)
        {
            return Result<SystemInitiator?>.Success(null);
        }

        return _engines.TryGetValue(handler.GetType(), out var engine) && engine == claimed
            ? Result<SystemInitiator?>.Success(claimed)
            : Result<SystemInitiator?>.Failure(
                JobErrorCodes.EngineNotAllowed,
                $"The handler of '{handler.Type}' jobs claims to run as the '{claimed.Component}' engine, which only Core's own handlers may.");
    }
}

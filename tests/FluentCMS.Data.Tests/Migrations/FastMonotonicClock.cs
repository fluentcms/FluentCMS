using System.Diagnostics;
using FluentCMS.Core.Caching;
using FluentCMS.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Data.Tests.Migrations;

// A monotonic clock that runs a thousand times fast, from zero so that it does not overflow, beside a wall clock that is
// real or stands still. Timers stay real.
internal sealed class FastMonotonicClock(DateTimeOffset? frozenWallClock) : TimeProvider
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public override DateTimeOffset GetUtcNow() => frozenWallClock ?? base.GetUtcNow();

    public override long GetTimestamp() => (Stopwatch.GetTimestamp() - _start) * 1000;

    // The host's lock, on this clock alone, registered before AddFluentCms so that core's default gives way to it.
    public static void Lock(IServiceCollection services, TimeProvider clock) =>
        services.AddSingleton<IDistributedLock>(provider => new DatabaseDistributedLock(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            provider.GetRequiredService<ILogger<DatabaseDistributedLock>>()));
}

using System.Data.Common;
using FluentCMS.Core.Caching;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// Work on the content tables that keeps SQLite's write transaction for longer than the lease of the lock it holds: the
// first command that adds a column waits, inside the transaction, until a renewal of the lock gave up waiting for it and
// read the row, and the commands after it run as they would. The lock has a lease of 300 milliseconds on the real clock,
// and its writes give up after the first busy timeout, about 5 seconds, where a host's wait about 35.
internal sealed class LongTransaction : DbCommandInterceptor
{
    private readonly TaskCompletionSource _renewalGaveUp = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _held;

    // Registered before AddFluentCms, so that core's default lock gives way to it.
    public static void Lock(IServiceCollection services) =>
        services.AddSingleton<IDistributedLock>(provider => new DatabaseDistributedLock(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            provider.GetRequiredService<ILogger<DatabaseDistributedLock>>(),
            provider.GetRequiredService<LeaseRenewalInterceptor>())
        {
            Lease = TimeSpan.FromMilliseconds(300),
        });

    public void Configure(FluentCmsBuilder cms)
    {
        cms.Services.ConfigureDbContext<SystemDbContext>(options => options
            .UseSqlite(sqlite => sqlite.CommandTimeout(1))
            .AddInterceptors(new Renewals(this)));
        cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(this));
    }

    public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase) && Interlocked.Exchange(ref _held, 1) == 0)
        {
            await _renewalGaveUp.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }

        return result;
    }

    // A renewal that gave up reads the row; nothing else reads it while the command waits.
    private sealed class Renewals(LongTransaction work) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref work._held) == 1 && command.CommandText.Contains("\"cms_lock\"", StringComparison.Ordinal))
            {
                work._renewalGaveUp.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }
    }
}

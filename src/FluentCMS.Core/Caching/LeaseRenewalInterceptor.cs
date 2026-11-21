using System.Collections.Concurrent;
using System.Data.Common;
using FluentCMS.Core.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Caching;

// On a database with one writer, a write transaction of the host keeps the renewals of the locks it holds waiting, and when
// it commits, a waiter that found a lease over can get the writer before them. So the host renews a lock it holds inside
// each of its write transactions as it commits, once half the lease has passed: no other writer comes between that renewal
// and the commit, and a holder whose process stopped commits nothing.
internal sealed class LeaseRenewalInterceptor : DbTransactionInterceptor
{
    private readonly ConcurrentDictionary<DatabaseDistributedLock.Handle, byte> _held = new();

    // On the host's contexts only where the database has one writer; elsewhere a renewal waits only for the lock's row.
    public static void AddTo(DbContextOptionsBuilder options, IServiceProvider services, IDataProvider provider)
    {
        if (provider.HasOneWriter)
        {
            options.AddInterceptors(services.GetRequiredService<LeaseRenewalInterceptor>());
        }
    }

    public void Add(DatabaseDistributedLock.Handle handle) => _held.TryAdd(handle, 0);

    public void Remove(DatabaseDistributedLock.Handle handle) => _held.TryRemove(handle, out _);

    public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        foreach (var (handle, _) in _held)
        {
            if (handle.RenewalDue)
            {
                handle.RenewWithin(transaction);
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        foreach (var (handle, _) in _held)
        {
            if (handle.RenewalDue)
            {
                await handle.RenewWithinAsync(transaction);
            }
        }

        return result;
    }
}

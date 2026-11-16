using System.Data.Common;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Data.Tests.Migrations;

// A lock that another holder takes from its holder during the work on the content tables: as the first command that adds a
// column runs, or once the transaction of the operations has committed. Its handle's Lost is canceled then.
internal sealed class LostLock(bool afterTheOperations) : IDistributedLock, IDisposable
{
    private readonly CancellationTokenSource _lost = new();
    private readonly bool _afterTheOperations = afterTheOperations;
    private int _altered;

    // Registered before AddFluentCms, so that core's default lock gives way to it.
    public void Lock(IServiceCollection services) => services.AddSingleton<IDistributedLock>(this);

    public void Configure(FluentCmsBuilder cms) =>
        cms.Services.ConfigureDbContext<CmsDbContext>(options => options.AddInterceptors(new Commands(this), new Transactions(this)));

    public void Dispose() => _lost.Dispose();

    public Task<IDistributedLockHandle?> TryAcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        Task.FromResult<IDistributedLockHandle?>(new Handle(name, _lost.Token));

    private sealed class Handle(string name, CancellationToken lost) : IDistributedLockHandle
    {
        public string Name => name;

        public CancellationToken Lost => lost;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Commands(LostLock owner) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref owner._altered, 1) == 0
                && !owner._afterTheOperations)
            {
                await owner._lost.CancelAsync();
            }

            return result;
        }
    }

    private sealed class Transactions(LostLock owner) : DbTransactionInterceptor
    {
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (owner._afterTheOperations && Volatile.Read(ref owner._altered) == 1)
            {
                await owner._lost.CancelAsync();
            }
        }
    }
}

using FluentCMS.Core.Persistence;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Infrastructure.Persistence;

// One database transaction per outermost scope; inner scopes flush into it and cannot commit it on their own.
internal sealed class UnitOfWork(FluentCmsDbContext context, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private readonly List<Func<CancellationToken, Task>> _afterRollback = [];
    private IDbContextTransaction? _transaction;
    private int _depth;
    private bool _abandoned;

    public async Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_depth == 0)
        {
            _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            _abandoned = false;
        }

        _depth++;
        return new Scope(this);
    }

    public void AfterRollback(Func<CancellationToken, Task> step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (_depth > 0)
        {
            _afterRollback.Add(step);
        }
    }

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_abandoned)
        {
            throw new InvalidOperationException("An inner unit of work was disposed without committing, so the outer one cannot commit.");
        }

        await context.SaveChangesAsync(cancellationToken);

        if (_depth == 1)
        {
            await _transaction!.CommitAsync(cancellationToken);
        }
    }

    private async ValueTask EndAsync(bool committed)
    {
        _depth--;
        _abandoned |= !committed;

        if (_depth > 0)
        {
            return;
        }

        List<Func<CancellationToken, Task>> steps = _abandoned ? [.. _afterRollback] : [];
        _afterRollback.Clear();

        // Disposing an uncommitted transaction rolls it back; the tracker is cleared so nothing from it leaks into the next scope.
        await _transaction!.DisposeAsync();
        _transaction = null;

        if (_abandoned)
        {
            context.ChangeTracker.Clear();
        }

        // Each step begins a unit of work of its own; a failing one is logged, because this runs while the caller's scope ends.
        foreach (var step in steps)
        {
            try
            {
                await step(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A step that runs after a rolled-back unit of work failed.");
            }
        }
    }

    private sealed class Scope(UnitOfWork owner) : IUnitOfWorkScope
    {
        private bool _committed;
        private bool _ended;

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_committed || _ended)
            {
                throw new InvalidOperationException("This unit of work scope has already been committed or disposed.");
            }

            await owner.CommitAsync(cancellationToken);
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            await owner.EndAsync(_committed);
        }
    }
}

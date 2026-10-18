using FluentCMS.Core.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace FluentCMS.Infrastructure.Persistence;

// One database transaction per outermost scope; inner scopes flush into it and cannot commit it on their own.
internal sealed class UnitOfWork(FluentCmsDbContext context) : IUnitOfWork
{
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

        // Disposing an uncommitted transaction rolls it back; the tracker is cleared so nothing from it leaks into the next scope.
        await _transaction!.DisposeAsync();
        _transaction = null;

        if (_abandoned)
        {
            context.ChangeTracker.Clear();
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

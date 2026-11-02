using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Content;

// Writes outbox rows in the transaction a context has open, so that they commit or roll back with it. Only these rows are
// saved: nothing the caller tracked is, and a failed save leaves nothing behind to be saved by the next write.
internal static class OutboxStaging
{
    public static async ValueTask SaveAsync(DbContext context, IReadOnlyList<OutboxMessage> rows, CancellationToken cancellationToken)
    {
        var held = HeldChanges.Take(context);
        try
        {
            context.AddRange(rows);
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            held.Release();
        }
    }
}

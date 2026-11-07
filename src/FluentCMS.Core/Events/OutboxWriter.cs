using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

// Records events for the outbox handlers that want them. Staging joins the transaction of the caller's context, so
// that the rows commit or roll back with the write that raised the events; recording writes on a context of its own.
internal sealed class OutboxWriter(IServiceScopeFactory scopes, OutboxRows rows, OutboxKick kick)
{
    public const int HandlerMaxLength = 128;

    private bool _staged;

    public async ValueTask StageAsync(DbContext context, IEnumerable<CmsEvent> events, CancellationToken cancellationToken)
    {
        var built = await rows.BuildAsync(events, cancellationToken);
        if (built.Count == 0)
        {
            return;
        }

        await OutboxStaging.SaveAsync(context, built, cancellationToken);
        _staged = true;
    }

    public async ValueTask RecordAsync(IEnumerable<CmsEvent> events, CancellationToken cancellationToken)
    {
        var built = await rows.BuildAsync(events, cancellationToken);
        if (built.Count == 0)
        {
            return;
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            var own = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
            own.AddRange(built);
            await own.SaveChangesAsync(cancellationToken);
        }

        _staged = true;
    }

    // After the commit: asks for the rows to be delivered at once. A failure waits for the scheduled drain.
    public async ValueTask KickIfStagedAsync()
    {
        if (!_staged)
        {
            return;
        }

        _staged = false;
        await kick.KickAsync();
    }
}

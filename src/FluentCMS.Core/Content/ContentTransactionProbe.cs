using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;

namespace FluentCMS.Core.Content;

// Tells the event bus whether the request is inside a transaction that nobody here controls.
internal sealed class ContentTransactionProbe(CmsDbContext context) : ITransactionProbe
{
    public bool InTransaction => context.Database.CurrentTransaction is not null;
}

using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;

namespace FluentCMS.Core.Content;

// Tells the event bus whether the request is inside a transaction that nobody here controls, on either context.
internal sealed class ContentTransactionProbe(CmsDbContext content, SystemDbContext system) : ITransactionProbe
{
    public bool InTransaction => content.Database.CurrentTransaction is not null || system.Database.CurrentTransaction is not null;
}

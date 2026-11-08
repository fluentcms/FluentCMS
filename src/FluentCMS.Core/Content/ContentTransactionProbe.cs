using FluentCMS.Core.Events;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;

namespace FluentCMS.Core.Content;

// Tells the event bus whether the request is inside a transaction that nobody here controls, on a core context or on a
// plugin's.
internal sealed class ContentTransactionProbe(CmsDbContext content, SystemDbContext system, PluginTransactions plugins) : ITransactionProbe
{
    public bool InTransaction => content.Database.CurrentTransaction is not null || system.Database.CurrentTransaction is not null || plugins.InTransaction;
}

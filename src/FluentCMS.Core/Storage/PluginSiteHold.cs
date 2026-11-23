using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Plugins;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

// PluginDbContext<T>.HoldSiteAsync: the shared lock of the site's row that a content write adding an entry takes, on the
// plugin's own connection in its own transaction, which the deletion of the site waits for or is waited for by (#3586).
internal sealed class PluginSiteHold(IDataProvider provider) : IPluginSiteHold
{
    public async Task HoldAsync(DbContext context, Guid site, CancellationToken cancellationToken)
    {
        if (!await provider.LockSiteAsync(context, site, exclusive: false, cancellationToken))
        {
            throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {site}.")]);
        }
    }
}

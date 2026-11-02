using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Localization;

// The locale allow-list of each site, read from cms_site_locale.
internal sealed class LocaleRegistry(CmsDbContext context)
{
    public async Task<SiteLocales> GetAsync(SiteId site, CancellationToken cancellationToken = default)
    {
        var id = site.Value;
        var defaultLocale = await context.Set<Site>().AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => item.DefaultLocale)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {site}.")]);

        var locales = await context.Set<SiteLocale>().AsNoTracking().Where(item => item.SiteId == id).ToListAsync(cancellationToken);
        return new SiteLocales(defaultLocale, locales);
    }
}

using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Localization;

// The locale allow-list of each site, read from cms_site_locale and kept in SiteCache.
internal sealed class LocaleRegistry(CmsDbContext context, SiteCache cache)
{
    public async Task<SiteLocales> GetAsync(SiteId site, CancellationToken cancellationToken = default)
    {
        var id = site.Value;
        var key = SiteCache.LocalesKey(id);
        if (cache.TryGet<SiteLocales>(key, out var cached))
        {
            return cached;
        }

        var generation = cache.Generation;
        var defaultLocale = await context.Set<Site>().AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => item.DefaultLocale)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {site}.")]);

        var rows = await context.Set<SiteLocale>().AsNoTracking().Where(item => item.SiteId == id).ToListAsync(cancellationToken);
        var locales = new SiteLocales(defaultLocale, rows);
        cache.Set(key, locales, generation);
        return locales;
    }
}

using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Sites;

// The default locale of a site for an operation no request carries, as an outbox delivery or a job runs, read through
// SiteCache so that a batch of them reads it once. Null when the site is gone, which is not kept.
internal static class SiteDefaultLocale
{
    public static async Task<string?> ReadAsync(IServiceProvider services, Guid site)
    {
        var cache = services.GetRequiredService<SiteCache>();
        var key = SiteCache.DefaultLocaleKey(site);
        if (cache.TryGet<string>(key, out var cached))
        {
            return cached;
        }

        var generation = cache.Generation;
        var locale = await services.GetRequiredService<SystemDbContext>().Set<Site>().AsNoTracking()
            .Where(row => row.Id == site)
            .Select(row => row.DefaultLocale)
            .FirstOrDefaultAsync();

        if (locale is not null)
        {
            cache.Set(key, locale, generation);
        }

        return locale;
    }
}

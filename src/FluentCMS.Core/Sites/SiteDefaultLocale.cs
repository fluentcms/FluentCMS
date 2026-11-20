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
        var context = services.GetRequiredService<SystemDbContext>();
        return await services.GetRequiredService<SiteCache>().GetOrLoadAsync(
            SiteCache.DefaultLocaleKey(site),
            token => context.Set<Site>().AsNoTracking()
                .Where(row => row.Id == site)
                .Select(row => row.DefaultLocale)
                .FirstOrDefaultAsync(token),
            locale => locale is not null,
            CancellationToken.None);
    }
}

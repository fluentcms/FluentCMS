using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Sites;

// The first of these that names a site wins, and a later one is not looked at: the API key binding, the header, the
// parameter, the host (the multi-site plugin's resolvers), the default site. What it finds is kept in SiteCache. A key, a
// header or a parameter that names no site is refused; a host name whose site is gone is a host name no site has, which
// reads the default site, since a resolver may still hold the host of a site that was deleted.
internal sealed class SiteResolver(CmsDbContext context, IEnumerable<ISiteHostResolver> hosts, SiteCache cache) : ISiteResolver
{
    public async ValueTask<SiteId> ResolveAsync(SiteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ApiKeySite is { } bound)
        {
            return await ExistingAsync(bound, "apiKey", cancellationToken);
        }

        if (Present(request.Header) is { } header)
        {
            return await NamedAsync(header, "X-Cms-Site", cancellationToken);
        }

        if (Present(request.Parameter) is { } parameter)
        {
            return await NamedAsync(parameter, "site", cancellationToken);
        }

        if (Present(request.Host) is { } host)
        {
            foreach (var resolver in hosts)
            {
                if (await resolver.ResolveAsync(host, cancellationToken) is { } found && await ExistsAsync(found, cancellationToken))
                {
                    return found;
                }
            }
        }

        if (cache.TryGet<Guid>(SiteCache.DefaultKey, out var cached))
        {
            return new SiteId(cached);
        }

        var generation = cache.Generation;
        var defaultSite = await context.Set<Site>().AsNoTracking()
            .Where(site => site.IsDefault)
            .Select(site => (Guid?)site.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultSite is not { } id)
        {
            throw new InvalidOperationException("The database has no default site.");
        }

        cache.Set(SiteCache.DefaultKey, id, generation);
        return new SiteId(id);
    }

    private async ValueTask<SiteId> NamedAsync(string value, string source, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(value, out var id))
        {
            return await ExistingAsync(new SiteId(id), source, cancellationToken);
        }

        var slug = value.ToLowerInvariant();
        var key = SiteCache.SlugKey(slug);
        if (cache.TryGet<Guid>(key, out var cached))
        {
            return new SiteId(cached);
        }

        var generation = cache.Generation;
        var bySlug = await context.Set<Site>().AsNoTracking()
            .Where(site => site.Slug == slug)
            .Select(site => (Guid?)site.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (bySlug is not { } found)
        {
            throw NotFound(source, $"There is no site '{value}'.");
        }

        cache.Set(key, found, generation);
        return new SiteId(found);
    }

    private async ValueTask<SiteId> ExistingAsync(SiteId site, string source, CancellationToken cancellationToken) =>
        await ExistsAsync(site, cancellationToken) ? site : throw NotFound(source, $"There is no site {site}.");

    private async ValueTask<bool> ExistsAsync(SiteId site, CancellationToken cancellationToken)
    {
        var id = site.Value;
        var key = SiteCache.SiteKey(id);
        if (cache.TryGet<bool>(key, out _))
        {
            return true;
        }

        var generation = cache.Generation;
        if (!await context.Set<Site>().AsNoTracking().AnyAsync(item => item.Id == id, cancellationToken))
        {
            return false;
        }

        cache.Set(key, true, generation);
        return true;
    }

    private static ValidationException NotFound(string source, string message) => new([new ValidationError(source, SiteErrorCodes.SiteNotFound, message)]);

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

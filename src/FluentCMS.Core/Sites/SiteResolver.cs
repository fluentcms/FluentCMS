using FluentCMS.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Sites;

// The first of these that names a site wins, and a later one is not looked at: the API key binding, the header, the
// parameter, the host (the multi-site plugin's resolvers), the default site.
internal sealed class SiteResolver(CmsDbContext context, IEnumerable<ISiteHostResolver> hosts) : ISiteResolver
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
                if (await resolver.ResolveAsync(host, cancellationToken) is { } found)
                {
                    return await ExistingAsync(found, "host", cancellationToken);
                }
            }
        }

        var defaultSite = await context.Set<Site>().AsNoTracking()
            .Where(site => site.IsDefault)
            .Select(site => (Guid?)site.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return defaultSite is { } id ? new SiteId(id) : throw new InvalidOperationException("The database has no default site.");
    }

    private async ValueTask<SiteId> NamedAsync(string value, string source, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(value, out var id))
        {
            return await ExistingAsync(new SiteId(id), source, cancellationToken);
        }

        var slug = value.ToLowerInvariant();
        var bySlug = await context.Set<Site>().AsNoTracking()
            .Where(site => site.Slug == slug)
            .Select(site => (Guid?)site.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return bySlug is { } found ? new SiteId(found) : throw NotFound(source, $"There is no site '{value}'.");
    }

    private async ValueTask<SiteId> ExistingAsync(SiteId site, string source, CancellationToken cancellationToken)
    {
        var id = site.Value;
        return await context.Set<Site>().AsNoTracking().AnyAsync(item => item.Id == id, cancellationToken)
            ? site
            : throw NotFound(source, $"There is no site {site}.");
    }

    private static ValidationException NotFound(string source, string message) => new([new ValidationError(source, SiteErrorCodes.SiteNotFound, message)]);

    private static string? Present(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

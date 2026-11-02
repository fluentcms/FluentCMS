using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Core.Localization;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Sites;

// What creating and deleting a site does to the rows around it. The Management endpoints (M2) call it.
internal sealed partial class SiteManager(CmsDbContext context, TimeProvider time)
{
    private const int SlugLength = 64;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    // The site, its default locale and its Public role go in one save, so a site never exists without its Public role.
    public async Task<Site> CreateAsync(string name, string slug, string defaultLocale, CancellationToken cancellationToken = default)
    {
        List<ValidationError> errors = [];
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new("name", SiteErrorCodes.InvalidSite, "A site needs a name."));
        }

        if (slug.Length > SlugLength || !SlugPattern().IsMatch(slug))
        {
            errors.Add(new("slug", SiteErrorCodes.InvalidSite, $"A slug is lower case letters and digits joined by hyphens, up to {SlugLength} characters."));
        }
        else if (await context.Set<Site>().AnyAsync(site => site.Slug == slug, cancellationToken))
        {
            errors.Add(new("slug", SiteErrorCodes.InvalidSite, $"A site with the slug '{slug}' exists."));
        }

        if (!LocaleTag.TryNormalize(defaultLocale, out var locale))
        {
            errors.Add(new("defaultLocale", LocaleErrorCodes.InvalidLocale, $"'{defaultLocale}' is not a well-formed BCP 47 language tag."));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var site = new Site
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Slug = slug,
            DefaultLocale = locale,
            IsDefault = false,
            CreatedAt = time.GetUtcNow(),
        };

        context.Add(site);
        context.Add(new SiteLocale { SiteId = site.Id, Code = locale, Position = 0, Enabled = true });
        context.Add(RoleSeed.PublicOf(Guid.CreateVersion7(), site.Id));
        await context.SaveChangesAsync(cancellationToken);
        return site;
    }

    // The foreign keys delete the site's locales and its roles, with their grants. A site with documents stays, because
    // they would be left in a site that no longer exists; trashed ones count, since restoring one needs its site.
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await context.Set<Site>().AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteNotFound, $"There is no site {id}.")]);

        if (site.IsDefault)
        {
            throw new ValidationException([new ValidationError("site", SiteErrorCodes.DefaultSiteProtected, "The default site cannot be deleted.")]);
        }

        if (await context.Documents.IgnoreQueryFilters().AnyAsync(document => document.SiteId == id, cancellationToken))
        {
            throw new ValidationException([new ValidationError("site", SiteErrorCodes.SiteHasContent, $"The site '{site.Slug}' still has content. Delete it first.")]);
        }

        await context.Set<Site>().Where(item => item.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}

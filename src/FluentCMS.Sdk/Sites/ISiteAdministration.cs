namespace FluentCMS.Sites;

/// <summary>
/// Reads, creates and deletes sites, for a plugin that manages more than the default site, such as the multi-site plugin.
/// Every call needs <c>sites.manage</c> for the instance as a whole, which a role of one site or a grant limited to sites
/// does not hold, and runs inside an operation.
/// </summary>
public interface ISiteAdministration
{
    /// <summary>Reads a site.</summary>
    /// <param name="site">The id of the site.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The site, or <see langword="null"/> when there is none.</returns>
    Task<SiteInfo?> FindAsync(SiteId site, CancellationToken cancellationToken = default);

    /// <summary>Creates a site with its default locale and its Public role, and raises <see cref="Events.SiteCreatedEvent"/>.</summary>
    /// <param name="name">The name of the site.</param>
    /// <param name="slug">Its slug: lower case letters and digits joined by hyphens, unique in the instance.</param>
    /// <param name="defaultLocale">Its default locale, a BCP 47 language tag.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The site.</returns>
    /// <exception cref="Content.ValidationException">The name or the slug is refused (<c>FCMS2304</c>), or the locale (<c>FCMS2311</c>).</exception>
    Task<SiteInfo> CreateAsync(string name, string slug, string defaultLocale, CancellationToken cancellationToken = default);

    /// <summary>Deletes a site with its locales, roles, settings and plugin rows, and raises <see cref="Events.SiteDeletedEvent"/>.</summary>
    /// <param name="site">The id of the site.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the site is deleted.</returns>
    /// <exception cref="Content.ValidationException">There is no such site (<c>FCMS2301</c>), it is the default site (<c>FCMS2302</c>) or it still has content (<c>FCMS2303</c>).</exception>
    Task DeleteAsync(SiteId site, CancellationToken cancellationToken = default);
}

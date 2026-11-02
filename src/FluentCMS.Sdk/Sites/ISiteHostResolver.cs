namespace FluentCMS.Sites;

/// <summary>Maps a host name to a site. Core has no host names; the multi-site plugin registers an implementation, and <see cref="ISiteResolver"/> asks it after the header and the parameter.</summary>
public interface ISiteHostResolver
{
    /// <summary>Finds the site of a host name.</summary>
    /// <param name="host">The host name of the request.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The site, or <see langword="null"/> when this resolver does not know the host.</returns>
    ValueTask<SiteId?> ResolveAsync(string host, CancellationToken cancellationToken = default);
}

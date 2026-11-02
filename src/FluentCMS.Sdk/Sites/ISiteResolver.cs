namespace FluentCMS.Sites;

/// <summary>Decides which site a request addresses.</summary>
public interface ISiteResolver
{
    /// <summary>Resolves the site. The order is the site of the API key, the <c>X-Cms-Site</c> header, the <c>site</c> parameter, the host name, and the default site.</summary>
    /// <param name="request">What the request says about its site.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The id of a site that exists.</returns>
    /// <exception cref="Content.ValidationException">The header or the parameter names no site.</exception>
    ValueTask<SiteId> ResolveAsync(SiteRequest request, CancellationToken cancellationToken = default);
}

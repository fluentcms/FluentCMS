namespace FluentCMS.Api.Core.Services;

public interface ISiteService
{
    Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Site> Add(Site site, CancellationToken cancellationToken = default);
    Task<Site> Update(Site site, CancellationToken cancellationToken = default);
    Task<Site> Remove(Guid id, CancellationToken cancellationToken = default);
}

internal class SiteService(ISiteRepository siteRepository, IEventPublisher messagePublisher) : ISiteService
{
    public async Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        return await siteRepository.GetAll(cancellationToken);
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await siteRepository.GetById(id, cancellationToken);
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        url = ValidateAndFormatUrl(url);
        // no need to check permissions
        return await siteRepository.GetByUrl(url, cancellationToken) ??
            throw new EntityNotFoundException<Site>();
    }

    public async Task<Site> Add(Site site, CancellationToken cancellationToken = default)
    {
        ValidateAndFormatUrls(site);

        // check if site url are unique
        var allSites = await siteRepository.GetAll(cancellationToken);
        if (allSites.Any(x => x.Urls.Any(y => site.Urls.Contains(y))))
            throw new EnhancedException(ExceptionCodes.SiteUrlMustBeUnique);

        await siteRepository.Add(site, cancellationToken);

        await messagePublisher.Publish(new SiteAddedEvent(site), cancellationToken);

        return site;
    }

    public async Task<Site> Update(Site site, CancellationToken cancellationToken = default)
    {
        ValidateAndFormatUrls(site);

        // check if site url is unique
        var allSites = await siteRepository.GetAll(cancellationToken);
        if (allSites.Any(x => x.Id != site.Id && x.Urls.Any(y => site.Urls.Contains(y))))
            throw new EnhancedException(ExceptionCodes.SiteUrlMustBeUnique);

        await siteRepository.Update(site, cancellationToken);

        await messagePublisher.Publish(new SiteUpdatedEvent(site), cancellationToken);

        return site;
    }

    public async Task<Site> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        var deletedSite = await siteRepository.Remove(id, cancellationToken);

        await messagePublisher.Publish(new SiteRemovedEvent(deletedSite), cancellationToken);

        return deletedSite;
    }


    private static void ValidateAndFormatUrls(Site site)
    {
        site.Urls = [.. site.Urls.Select(ValidateAndFormatUrl)];
    }

    public const string VALID_DOMAIN_NAME_REGEX = @"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])(:\d{1,5})?$";

    private static string ValidateAndFormatUrl(string url)
    {
        // Trim spaces
        url = url.Trim();

        // Check if URL is empty
        if (string.IsNullOrEmpty(url))
            throw new EnhancedException(ExceptionCodes.SiteUrlIsEmpty);

        // Convert to lowercase
        url = url.ToLowerInvariant();

        // if the url ends with a slash, remove it
        if (url.EndsWith('/'))
            url = url[..^1];

        // Validate URL format using regex
        if (!Regex.IsMatch(url, VALID_DOMAIN_NAME_REGEX))
            throw new EnhancedException(ExceptionCodes.SiteUrlIsInvalid);

        // Return the formatted URL if valid
        return url;
    }

}

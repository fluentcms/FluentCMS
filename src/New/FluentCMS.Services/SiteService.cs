namespace FluentCMS.Services;

public interface ISiteService : IService
{
    Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Site> Create(Site site, CancellationToken cancellationToken = default);
    Task<Site> Update(Site site, CancellationToken cancellationToken = default);
    Task<Site> Delete(Guid id, CancellationToken cancellationToken = default);
}

public class SiteService(ISiteRepository siteRepository) : ISiteService
{

    public async Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        return await siteRepository.GetAll(cancellationToken);
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await siteRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return await siteRepository.GetByUrl(url, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);
    }

    public async Task<Site> Create(Site site, CancellationToken cancellationToken = default)
    {
        PrepareSite(site);

        // check if site url are unique
        var allSites = await siteRepository.GetAll(cancellationToken);
        if (allSites.Any(x => x.Urls.Any(y => site.Urls.Contains(y))))
            throw new AppException(ExceptionCodes.SiteUrlMustBeUnique);

        // create the site or throw an exception if it fails
        var newSite = await siteRepository.Create(site, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteUnableToCreate);

        return newSite;
    }

    public async Task<Site> Update(Site site, CancellationToken cancellationToken = default)
    {
        PrepareSite(site);

        // check if site url is unique
        var allSites = await siteRepository.GetAll(cancellationToken);
        if (allSites.Any(x => x.Id != site.Id && x.Urls.Any(y => site.Urls.Contains(y))))
            throw new AppException(ExceptionCodes.SiteUrlMustBeUnique);

        var updateSite = await siteRepository.Update(site, cancellationToken);

        return updateSite ?? throw new AppException(ExceptionCodes.SiteUnableToUpdate);
    }

    public async Task<Site> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        return await siteRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteUnableToDelete);
    }

    private static void PrepareSite(Site site)
    {
        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();
    }

}

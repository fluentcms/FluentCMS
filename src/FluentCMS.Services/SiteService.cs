using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface ISiteService
{
    Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Site> Create(Site site, CancellationToken cancellationToken = default);
    Task<Site> Update(Site site, CancellationToken cancellationToken = default);
    Task Delete(Site site, CancellationToken cancellationToken = default);
}

public class SiteService(
    ISiteRepository siteRepository,
    SitePolicies sitePolicies,
    IAuthorizationProvider authorizationProvider) : ISiteService
{

    public async Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await siteRepository.GetAll(cancellationToken);

        // only return sites that the user has access to edit
        return sites.Where(x => authorizationProvider.Authorize(x, sitePolicies.Editor));
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        // There is no need to check for permissions here
        return await siteRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        // There is no need to check for permissions here
        return await siteRepository.GetByUrl(url, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);
    }

    public async Task<Site> Create(Site site, CancellationToken cancellationToken = default)
    {
        // only super admin can create a site
        if (!authorizationProvider.Authorize(site, sitePolicies.SuperAdmin))
            throw new AppPermissionException();

        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();

        // check if site url are unique
        var allSites = await siteRepository.GetAll(cancellationToken);
        if (allSites.Any(x => x.Urls.Any(y => site.Urls.Contains(y))))
            throw new AppException(ExceptionCodes.SiteUrlMustBeUnique);

        // create the site or throw an exception if it fails
        var newSite = await siteRepository.Create(site, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteUnableToCreate);

        // add admin permission to the site for the admin role
        await authorizationProvider.Create(newSite, Policies.SITE_ADMIN, cancellationToken);

        // add edit permission to the site for the edit role
        await authorizationProvider.Create(newSite, Policies.SITE_EDITOR, cancellationToken);

        return newSite;
    }

    public async Task<Site> Update(Site site, CancellationToken cancellationToken = default)
    {
        // Checking if the user has access to update the site
        if (!authorizationProvider.Authorize(site, sitePolicies.Editor))
            throw new AppPermissionException();

        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();

        // check if site url is unique
        var allSites = await siteRepository.GetAll(cancellationToken);
        if (allSites.Any(x => x.Id != site.Id && x.Urls.Any(y => site.Urls.Contains(y))))
            throw new AppException(ExceptionCodes.SiteUrlMustBeUnique);

        var updateSite = await siteRepository.Update(site, cancellationToken);

        return updateSite ?? throw new AppException(ExceptionCodes.SiteUnableToUpdate);
    }

    public async Task Delete(Site site, CancellationToken cancellationToken = default)
    {
        // only super admin can delete a site
        if (!authorizationProvider.Authorize(site, sitePolicies.SuperAdmin))
            throw new AppPermissionException();

        _ = await siteRepository.Delete(site.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteUnableToDelete);
    }

}

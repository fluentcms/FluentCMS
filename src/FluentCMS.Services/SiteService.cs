using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface ISiteService
{
    Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Site> Create(Site site, CancellationToken cancellationToken = default);
    Task<Site> Update(Site site, CancellationToken cancellationToken = default);
    // TODO: argument should be Guid id instead of Site site
    // TODO: it should returns Task<Site> instead of Task
    Task Delete(Site site, CancellationToken cancellationToken = default);
}

public class SiteService : BaseService<Site>, ISiteService
{
    private readonly ISiteRepository _siteRepository;

    public SiteService(ISiteRepository repository, IApplicationContext appContext) : base(appContext)
    {
        _siteRepository = repository;
    }

    public async Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        if (!Current.IsAuthenticated)
            throw new ApplicationException("You are not authenticated.");

        var sites = await _siteRepository.GetAll(cancellationToken);

        // Checking if the user is super admin
        if (Current.IsSuperAdmin)
            return sites;

        // filter sites based on the user roles
        sites = sites.Where(x => Current.IsInRole(x.AdminRoleIds));

        return sites;
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await _siteRepository.GetById(id, cancellationToken) ??
            throw new ApplicationException("Requested site does not exists.");
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return await _siteRepository.GetByUrl(url, cancellationToken) ??
            throw new ApplicationException("Requested site does not exists.");
    }

    public async Task<Site> Create(Site site, CancellationToken cancellationToken = default)
    {
        if (!Current.IsSuperAdmin)
            throw new ApplicationException("Only super admin can create a site.");

        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();

        // check if the site URLs are unique in all sites
        if (await _siteRepository.CheckUrls(site.Urls, cancellationToken))
            throw new ApplicationException("Site URLs must be unique");

        PrepareForCreate(site);

        var newSite = await _siteRepository.Create(site, cancellationToken);
        return newSite ?? throw new ApplicationException("Site not created");
    }

    public async Task<Site> Update(Site site, CancellationToken cancellationToken = default)
    {
        // Checking if the user has access to update the site
        if (!Current.IsSuperAdmin || !Current.IsInRole(site.AdminRoleIds))
            throw new ApplicationException("Only super admin or admin can update a site.");

        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();

        // check if the site URLs are unique in all sites
        if (await _siteRepository.CheckUrls(site.Urls, cancellationToken))
            throw new ApplicationException("Site URLs must be unique");

        PrepareForUpdate(site);

        var updateSite = await _siteRepository.Update(site, cancellationToken);

        return updateSite ?? throw new ApplicationException("Site not updated");
    }

    public async Task Delete(Site site, CancellationToken cancellationToken = default)
    {
        // Checking if the user is super user
        if (!Current.IsSuperAdmin)
            throw new ApplicationException("Only super admin can delete a site.");

        // TODO: all pages should be deleted either by cascade or manually

        var deletedSite = await _siteRepository.Delete(site.Id, cancellationToken);
        if (deletedSite is null) throw new ApplicationException("Site not deleted.");
    }
}

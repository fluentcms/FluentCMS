using FluentCMS.Entities.Sites;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface ISiteService
{
    Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<Site> Create(Site site, CancellationToken cancellationToken = default);
    Task<Site> Edit(Site site, CancellationToken cancellationToken = default);
    Task Delete(Site site, CancellationToken cancellationToken = default);
}

public class SiteService : ISiteService
{
    private readonly ISiteRepository _siteRepository;

    public SiteService(ISiteRepository repository)
    {
        _siteRepository = repository;
    }

    public async Task<IEnumerable<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await _siteRepository.GetAll(cancellationToken);
        return sites;
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await _siteRepository.GetById(id, cancellationToken)
            ?? throw new ApplicationException("Requested site does not exists.");
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return await _siteRepository.GetByUrl(url, cancellationToken)
            ?? throw new ApplicationException("Requested site does not exists.");
    }

    public async Task<Site> Create(Site site, CancellationToken cancellationToken = default)
    {
        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();

        // check if the site URLs are unique in all sites
        if (await _siteRepository.CheckUrls(site.Urls, cancellationToken))
            throw new ApplicationException("Site URLs must be unique");

        // TODO: check permissions, only super admin can create a site
        // Except for the first site, which is created by the system

        var newSite = await _siteRepository.Create(site, cancellationToken);
        return newSite ?? throw new ApplicationException("Site not created");
    }

    public async Task<Site> Edit(Site site, CancellationToken cancellationToken = default)
    {
        var updateSite = await _siteRepository.Update(site, cancellationToken);
        return updateSite ?? throw new ApplicationException("Site not updated");
    }

    public async Task Delete(Site site, CancellationToken cancellationToken = default)
    {
        // TODO: check permissions, only super admin can delete a site

        // TODO: all pages should be deleted either by cascade or manually

        var deletedSite = await _siteRepository.Delete(site.Id, cancellationToken);
        if (deletedSite is null) throw new ApplicationException("Site not deleted.");
    }
}
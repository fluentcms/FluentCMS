using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Application.Services;

public interface ISiteOtherService
{
    Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<List<Site>> GetAll(CancellationToken cancellationToken = default);
    Task<Site> GetById(Guid id, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
    Task<Site> Create(Site site, CancellationToken cancellationToken = default);
    Task<Site> Update(Site site, CancellationToken cancellationToken = default);
}

public class SiteOtherService : ISiteOtherService
{
    private readonly ISiteRepository _siteRepository;

    public SiteOtherService(ISiteRepository repository)
    {
        _siteRepository = repository;
    }

    public async Task<Site> Create(Site site, CancellationToken cancellationToken = default)
    {
        // normalizing the site URLs to lowercase
        site.Urls = site.Urls.Select(x => x.ToLower()).ToList();

        // check if the site URLs are unique in all sites
        if (await _siteRepository.CheckUrls(site.Urls, cancellationToken))
            throw new Exception("Site URLs must be unique");

        // TODO: check permissions, only super admin can create a site
        // Except for the first site, which is created by the system

        var newSite = await _siteRepository.Create(site, cancellationToken);
        return newSite ?? throw new Exception("Site not created");
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        // TODO: check permissions, only super admin can delete a site

        // TODO: all pages should be deleted either by cascade or manually

        _ = await _siteRepository.Delete(id, cancellationToken) ?? throw new Exception(id.ToString());
    }

    public async Task<List<Site>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await _siteRepository.GetAll(cancellationToken);
        return sites.ToList();
    }

    public async Task<Site> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await _siteRepository.GetById(id, cancellationToken) ?? throw new Exception(id.ToString());
    }

    public async Task<Site> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        return await _siteRepository.GetByUrl(url, cancellationToken) ?? throw new Exception(url);
    }

    public async Task<Site> Update(Site site, CancellationToken cancellationToken = default)
    {
        var updateSite = await _siteRepository.Update(site, cancellationToken);
        return updateSite ?? throw new Exception("Site not updated");
    }
}
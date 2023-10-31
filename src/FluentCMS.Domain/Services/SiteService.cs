using Ardalis.GuardClauses;
using FluentCMS.Entities.Sites;
using FluentCMS.Repository;

namespace FluentCMS.Services;
public class SiteService(ISiteRepository siteRepository)
{
    public async Task Create(Site site)
    {
        Guard.Against.Null(site);
        await siteRepository.Create(site);
    }

    public async Task<Site> GetById(Guid id)
    {
        Guard.Against.NullOrEmpty(id);
        return await siteRepository.GetById(id)
            ?? throw new ApplicationException("Provided SiteId does not exists.");
    }

    public async Task<Site> GetByUrl(string Url)
    {
        Guard.Against.NullOrEmpty(Url);
        return await siteRepository.GetByUrl(Url)
            ?? throw new ApplicationException("Provided Url does not exists.");
    }

    public async Task<IEnumerable<Site>> GetAll()
    {
        return await siteRepository.GetAll();
    }

    public async Task Update(Site site)
    {
        Guard.Against.Null(site);
        _ = await siteRepository.GetById(site.Id)
            ?? throw new ApplicationException("Provided SiteId does not exists.");
        await siteRepository.Update(site);
    }

    public async Task Delete(Guid id)
    {
        Guard.Against.NullOrEmpty(id);
        _ = await siteRepository.GetById(id)
            ?? throw new ApplicationException("Provided SiteId does not exists.");
        await siteRepository.Delete(id);
    }
}

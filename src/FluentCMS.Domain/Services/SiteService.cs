using Ardalis.GuardClauses;
using FluentCMS.Entities.Sites;
using FluentCMS.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Services;
public class SiteService(ISiteRepository siteRepository)
{
    private readonly ISiteRepository _siteRepository = siteRepository;

    public async Task Create(Site site)
    {
        Guard.Against.Null(site);
        await _siteRepository.Create(site);
    }

    public async Task<Site> GetById(Guid id)
    {
        Guard.Against.NullOrEmpty(id);
        return await _siteRepository.GetById(id)
               ?? throw new ApplicationException("Provided SiteId does not exists.");
    }
    public async Task<Site> GetByUrl(string Url)
    {
        Guard.Against.NullOrEmpty(Url);
        return await _siteRepository.GetByUrl(Url)
               ?? throw new ApplicationException("Provided Url does not exists.");
    }
    public async Task<IEnumerable<Site>> GetAll()
    {
        return await _siteRepository.GetAll();
    }
    
    public async Task Update(Site site)
    {
        Guard.Against.Null(site);
        _ = await _siteRepository.GetById(site.Id)
                       ?? throw new ApplicationException("Provided SiteId does not exists.");
        await _siteRepository.Update(site);
    }

    public async Task Delete(Guid id)
    {
        Guard.Against.NullOrEmpty(id);
        _ = await _siteRepository.GetById(id)
                       ?? throw new ApplicationException("Provided SiteId does not exists.");
        await _siteRepository.Delete(id);
    }
}

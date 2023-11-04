using FluentCMS.Application.Dtos.Pages;
using FluentCMS.Application.Dtos.Sites;
using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Application.Services;

public interface ISiteService
{
    Task<SiteDto> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<List<SiteDto>> GetAll(CancellationToken cancellationToken = default);
    Task<SiteDto> GetById(Guid id, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
    Task<SiteDto> Create(CreateSiteDto createSite, CancellationToken cancellationToken = default);
    Task<SiteDto> Update(UpdateSiteDto updateSite, CancellationToken cancellationToken = default);
}

public class SiteService : ISiteService
{
    private readonly ISiteRepository _siteRepository;
    private readonly IPageRepository _pageRepository;

    public SiteService(ISiteRepository repository, IPageRepository pageRepository)
    {
        _siteRepository = repository;
        _pageRepository = pageRepository;
    }

    public async Task<SiteDto> Create(CreateSiteDto createSite, CancellationToken cancellationToken = default)
    {
        var site = await _siteRepository.Create(new Site
        {
            Name = createSite.Name,
            Description = createSite.Description,
            Urls = createSite.Urls
        }, cancellationToken);

        return site == null ? throw new Exception("Site not created") : await GetSiteDto(site, cancellationToken);
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await _siteRepository.Delete(id, cancellationToken) ?? throw new Exception(id.ToString());
    }

    public async Task<List<SiteDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var sites = await _siteRepository.GetAll(cancellationToken);
        var siteDtos = new List<SiteDto>();
        foreach (var site in sites)
        {
            siteDtos.Add(await GetSiteDto(site, cancellationToken));
        }
        return siteDtos;
    }

    public async Task<SiteDto> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await _siteRepository.GetById(id, cancellationToken) ?? throw new Exception(id.ToString());

        return await GetSiteDto(site, cancellationToken);
    }

    public async Task<SiteDto> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        var site = await _siteRepository.GetByUrl(url, cancellationToken) ?? throw new Exception(url);

        return await GetSiteDto(site, cancellationToken);
    }

    public async Task<SiteDto> Update(UpdateSiteDto updateSite, CancellationToken cancellationToken = default)
    {
        var siteUpdate = new Site
        {
            Id = updateSite.Id,
            Name = updateSite.Name,
            Description = updateSite.Description,
            Urls = updateSite.Urls
        };

        var site = await _siteRepository.Update(siteUpdate, cancellationToken);

        return site == null ? throw new Exception(updateSite.Id.ToString()) : await GetSiteDto(site, cancellationToken);
    }

    private async Task<SiteDto> GetSiteDto(Site site, CancellationToken cancellationToken = default)
    {
        var pages = await _pageRepository.GetBySiteId(site.Id, cancellationToken);

        // TODO: add automapper
        var siteDto = new SiteDto
        {
            Id = site.Id,
            Name = site.Name,
            Description = site.Description,
            Urls = site.Urls,
            Pages = pages.Select(p => new PageDto // TODO: add automapper
            {
                Id = p.Id,
                Title = p.Title,
                Order = p.Order,
                Path = p.Path
            }).ToList()
        };

        return siteDto;
    }

}
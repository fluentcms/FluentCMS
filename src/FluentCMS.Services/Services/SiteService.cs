using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Application.Services;

public interface ISiteService
{
    Task<SiteDto> GetByUrl(string url, CancellationToken cancellationToken = default);
    Task<SiteDto> GetById(Guid id, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
    Task<SiteDto> Create(SiteDto siteDto, CancellationToken cancellationToken = default);
    Task<SiteDto> Update(SiteDto siteDto, CancellationToken cancellationToken = default);
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

    public Task<SiteDto> Create(SiteDto siteDto, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
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

    public Task<SiteDto> Update(SiteDto siteDto, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
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

public class SiteDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required List<string> Urls { get; set; } = [];
    public List<PageDto> Pages { get; set; } = [];
}

public class PageDto
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public List<Page> Children { get; set; } = [];
    public int Order { get; set; }
    public required string Path { get; set; }
}

public class RoleDto : Role
{

}
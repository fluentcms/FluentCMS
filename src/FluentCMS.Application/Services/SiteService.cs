using Ardalis.GuardClauses;
using FluentCMS.Application.Dtos.Sites;
using FluentCMS.Entities.Sites;
using FluentCMS.Repository;

namespace FluentCMS.Application.Services;

public interface ISiteService
{
    Task<SiteDto> GetById(Guid id);
    Task<SiteDto> GetByUrl(string Url);
    Task<SearchSiteResponse> Search(SearchSiteRequest request);
    Task<Guid> Create(CreateSiteRequest request);
    Task Edit(EditSiteRequest request);
    Task Delete(DeleteSiteRequest request);
    Task AddSiteUrl(AddSiteUrlRequest request);
    Task RemoveSiteUrl(RemoveSiteUrlRequest request);
}

internal class SiteService(AutoMapper.IMapper mapper, ISiteRepository siteRepository) : ISiteService
{
    public async Task<SiteDto> GetById(Guid id)
    {
        Guard.Against.Default(id);

        var site = await siteRepository.GetById(id)
            ?? throw new ApplicationException("Provided SiteId does not exists.");

        return mapper.Map<SiteDto>(site);
    }

    public async Task<SiteDto> GetByUrl(string Url)
    {
        Guard.Against.NullOrEmpty(Url);

        var site = await siteRepository.GetByUrl(Url)
            ?? throw new ApplicationException("Provided Url does not exists.");

        return mapper.Map<SiteDto>(site);
    }

    public async Task<SearchSiteResponse> Search(SearchSiteRequest request)
    {
        var data = await siteRepository.GetAll();
        return new SearchSiteResponse
        {
            Data = data.Select(x => mapper.Map<SiteDto>(x)),
            Total = data.Count(),
        };
    }

    public async Task<Guid> Create(CreateSiteRequest request)
    {
        Guard.Against.Null(request);
        Guard.Against.NullOrWhiteSpace(request.Name);

        var newId = Guid.NewGuid();
        var site = new Site(newId, request.Name, request.Description, request.URLs, request.RoleId);
        await siteRepository.Create(site);
        return newId;
    }

    public async Task Edit(EditSiteRequest request)
    {
        Guard.Against.Null(request);
        Guard.Against.NullOrWhiteSpace(request.Name);

        var site = await siteRepository.GetById(request.Id)
            ?? throw new ApplicationException("Provided SiteId does not exists.");

        if (site.Name != request.Name) site.SetName(request.Name);
        if (site.Description != request.Description) site.SetDescription(request.Description);
        if (site.RoleId != request.RoleId) site.SetRoleId(request.RoleId);
        site.Urls = request.URLs.ToList();
        await siteRepository.Update(site);
    }

    public async Task Delete(DeleteSiteRequest request)
    {
        Guard.Against.Null(request);
        Guard.Against.Default(request.Id);

        var site = await siteRepository.GetById(request.Id)
            ?? throw new ApplicationException("Provided SiteId does not exists.");

        await siteRepository.Delete(site.Id);
    }

    public async Task AddSiteUrl(AddSiteUrlRequest request)
    {
        Guard.Against.Null(request);
        Guard.Against.Default(request.SiteId);
        Guard.Against.NullOrWhiteSpace(request.NewUrl);

        var site = await siteRepository.GetById(request.SiteId)
            ?? throw new ApplicationException("Provided SiteId does not exists.");

        site.AddUrl(request.NewUrl);
        await siteRepository.Update(site);
    }

    public async Task RemoveSiteUrl(RemoveSiteUrlRequest request)
    {
        Guard.Against.Null(request);
        Guard.Against.Default(request.SiteId);
        Guard.Against.NullOrWhiteSpace(request.Url);

        var site = await siteRepository.GetById(request.SiteId)
            ?? throw new ApplicationException("Provided SiteId does not exists.");

        site.RemoveUrl(request.Url);
        await siteRepository.Update(site);
    }
}

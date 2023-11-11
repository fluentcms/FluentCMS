using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IPageService
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Page>> GetByParentId(Guid id);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
    Task<Page> Edit(Page page, CancellationToken cancellationToken = default);
    Task Delete(Page page, CancellationToken cancellationToken = default);
    Task<IEnumerable<Page>> GetBySiteIdAndParentId(Guid siteId, Guid? parentId = null);
}

public class PageService : IPageService
{
    private readonly IPageRepository _pageRepository;
    private readonly IApplicationContext _applicationContext;

    public PageService(IPageRepository pageRepository, IApplicationContext applicationContext)
    {
        _pageRepository = pageRepository;
        _applicationContext = applicationContext;
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        return await _pageRepository.GetBySiteId(siteId, cancellationToken);
    }

    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await _pageRepository.GetById(id, cancellationToken) ?? throw new Exception(id.ToString());

        return page;
    }

    public async Task<Page> Create(Page page, CancellationToken cancellationToken = default)
    {
        // TODO: check permissions, only admins can create a page
        // Except for the first site, which is created by the system

        // normalizing the page path to lowercase
        NormalizePath(page);
        await CheckForDuplicatePath(page);

        page.CreatedBy = _applicationContext.Current?.User?.UserName ?? string.Empty;
        page.LastUpdatedBy = _applicationContext.Current?.User?.UserName ?? string.Empty;

        var newPage = await _pageRepository.Create(page, cancellationToken);
        return newPage ?? throw new Exception("Page not created");
    }

    private async Task CheckForDuplicatePath(Page page)
    {
        // check if the page path is unique
        var samePathPage = await _pageRepository.GetByPath(page.Path);
        if (samePathPage != null && samePathPage?.Id != page.Id)
        {
            throw new ApplicationException("Page path must be unique");
        }
    }

    public async Task<Page> Edit(Page page, CancellationToken cancellationToken = default)
    {
        // TODO: check permissions, only admins can create a page
        // Except for the first site, which is created by the system

        // normalizing the page path to lowercase
        NormalizePath(page);

        // check if the page path is unique
        await CheckForDuplicatePath(page);

        var newPage = await _pageRepository.Update(page, cancellationToken);
        return newPage ?? throw new Exception("Page not created");
    }

    private static void NormalizePath(Page page)
    {
        page.Path = page.Path.ToLower();
    }

    public Task Delete(Page page, CancellationToken cancellationToken = default)
    {
        return _pageRepository.Delete(page.Id);
    }

    public Task<IEnumerable<Page>> GetByParentId(Guid id)
    {
        return _pageRepository.GetByParentId(id);
    }

    public async Task<IEnumerable<Page>> GetBySiteIdAndParentId(Guid siteId, Guid? parentId)
    {
        var sitePages = await _pageRepository.GetBySiteIdAndParentId(siteId);
        if (parentId is null)
        {
            return sitePages;
        }
        // todo: handle edge-case that multiple sites contain the same parent page
        var childPages = await _pageRepository.GetBySiteIdAndParentId(parentId.Value);
        return childPages;
    }
}
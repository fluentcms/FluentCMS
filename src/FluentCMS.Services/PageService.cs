using FluentCMS.Entities.Pages;
using FluentCMS.Entities.Sites;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IPageService
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
    Task<Page> Edit(Page page, CancellationToken cancellationToken = default);
    Task Delete(Page page, CancellationToken cancellationToken = default);
}

public class PageService : IPageService
{
    private readonly IPageRepository _pageRepository;

    public PageService(IPageRepository pageRepository)
    {
        _pageRepository = pageRepository;
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
}
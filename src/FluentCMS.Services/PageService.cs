using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IPageService
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
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
        page.Path = page.Path.ToLower();

        // TODO: check if the page path is unique

        page.CreatedBy = _applicationContext.Current?.User?.Username ?? string.Empty;
        page.LastUpdatedBy = _applicationContext.Current?.User?.Username ?? string.Empty;

        var newPage = await _pageRepository.Create(page, cancellationToken);
        return newPage ?? throw new Exception("Page not created");
    }
}
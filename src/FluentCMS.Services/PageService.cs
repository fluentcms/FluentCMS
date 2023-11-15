using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IPageService
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
    Task<Page> Update(Page page, CancellationToken cancellationToken = default);
    Task Delete(Page page, CancellationToken cancellationToken = default);
}

public class PageService : BaseService<Page>, IPageService
{
    private readonly IPageRepository _pageRepository;

    public PageService(IApplicationContext applicationContext, IPageRepository pageRepository) : base(applicationContext)
    {
        _pageRepository = pageRepository;
    }

    public async Task<Page> Create(Page page, CancellationToken cancellationToken = default)
    {
        if (!Current.IsSiteAdmin) throw new AppPermissionException();
        await BlockDuplicatePath(page);
        return await _pageRepository.Create(page, cancellationToken) ?? throw new AppException(ExceptionCodes.PageUnableToCreate);
    }

    public async Task Delete(Page page, CancellationToken cancellationToken = default)
    {
        if (IsPageAdmin(page)) throw new AppPermissionException();
        _ = await _pageRepository.Delete(page.Id, cancellationToken) ?? throw new AppException(ExceptionCodes.PageUnableToDelete);
    }


    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await _pageRepository.GetById(id, cancellationToken) ?? throw new AppException(ExceptionCodes.PageNotFound);
        if (!Current.IsInRole(page.ViewRoleIds) && !Current.IsSiteAdmin) throw new AppPermissionException();
        return page;
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        var pages = await _pageRepository.GetBySiteId(siteId, cancellationToken) ?? throw new AppException(ExceptionCodes.PageNotFound);
        return pages.Where(x => Current.IsInRole(x.ViewRoleIds) || Current.IsSiteAdmin);
    }

    public async Task<Page> Update(Page page, CancellationToken cancellationToken = default)
    {
        var previousPage = await _pageRepository.GetById(page.Id, cancellationToken) ?? throw new AppException(ExceptionCodes.PageNotFound);
        if (!IsPageAdmin(previousPage)) throw new AppPermissionException(); // we should check previous roles
        await BlockDuplicatePath(page);
        return await _pageRepository.Update(page, cancellationToken) ?? throw new AppException(ExceptionCodes.HostUnableToUpdate);
    }

    private async Task BlockDuplicatePath(Page page)
    {
        var matchingPath = await _pageRepository.GetByPath(page.Path);
        if (matchingPath != null && matchingPath.Id != page.Id)
        {
            throw new AppException(ExceptionCodes.PagePathMustBeUnique);
        }
    }

    private bool IsPageAdmin(Page page)
    {
        return Current.IsSiteAdmin || Current.IsInRole(page.AdminRoleIds);
    }
}

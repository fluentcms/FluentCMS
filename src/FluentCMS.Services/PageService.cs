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

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        var pages = await _pageRepository.GetBySiteId(siteId, cancellationToken);
        return pages.Where(HasViewPermissionForPage);
    }

    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await _pageRepository.GetById(id, cancellationToken)
            ?? throw new AppException(ExceptionCodes.PageNotFound);

        if (!HasViewPermissionForPage(page))
            throw new AppPermissionException();

        return page;
    }

    private bool HasViewPermissionForPage(Page page)
    {
        var permission = Current.IsInRole(page.ViewRoleIds);
        permission = permission || Current.IsInRole(page.ViewRoleIds);
        permission = permission || Current.IsInRole(page.AdminRoleIds);
        return permission;
    }

    public async Task<Page> Create(Page page, CancellationToken cancellationToken = default)
    {
        // TODO: check permissions, only admins can create a page
        // Except for the first site, which is created by the system
        CheckCreatePermission();

        // normalizing the page path to lowercase
        NormalizePath(page);
        await CheckForDuplicatePath(page);

        page.CreatedBy = Current?.User?.UserName ?? string.Empty;
        page.LastUpdatedBy = Current?.User?.UserName ?? string.Empty;

        var newPage = await _pageRepository.Create(page, cancellationToken);
        return newPage ?? throw new AppException(ExceptionCodes.PageUnableToCreate);
    }

    private void CheckCreatePermission()
    {
        if (Current.IsInRole(Current.Site.AdminRoleIds))
        {
            throw new AppPermissionException();
        }
    }

    private async Task CheckForDuplicatePath(Page page)
    {
        // check if the page path is unique
        var samePathPage = await _pageRepository.GetByPath(page.Path);
        if (samePathPage != null && samePathPage?.Id != page.Id)
        {
            throw new AppException(ExceptionCodes.PagePathMustBeUnique);
        }
    }

    public async Task<Page> Update(Page page, CancellationToken cancellationToken = default)
    {
        // TODO: check permissions, only admins can create a page
        // Except for the first site, which is created by the system
        CheckPageOrAdminPermission(page);
        // normalizing the page path to lowercase
        NormalizePath(page);

        // check if the page path is unique
        await CheckForDuplicatePath(page);

        var newPage = await _pageRepository.Update(page, cancellationToken);
        return newPage ?? throw new AppException(ExceptionCodes.PageUnableToUpdate);
    }

    private void CheckPageOrAdminPermission(Page page)
    {
        if (!Current.IsInRole(Current.Site.AdminRoleIds) && !Current.IsInRole(page.AdminRoleIds))
            throw new AppPermissionException();
    }

    // TODO: should we replace space with - or _?
    private static void NormalizePath(Page page)
    {
        page.Path = page.Path.Trim().Replace(" ", "-").ToLower();
    }

    public Task Delete(Page page, CancellationToken cancellationToken = default)
    {
        CheckPageOrAdminPermission(page);
        return _pageRepository.Delete(page.Id, cancellationToken)
            ?? throw new AppException(ExceptionCodes.PageUnableToDelete);
    }
}

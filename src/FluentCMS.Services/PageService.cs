using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace FluentCMS.Services;

public interface IPageService
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Page>> GetByParentId(Guid id);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
    Task<Page> Update(Page page, CancellationToken cancellationToken = default);
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
        var pages = await _pageRepository.GetBySiteId(siteId, cancellationToken);
        return pages.Where(HasViewPermissionForPage);
    }

    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await _pageRepository.GetById(id, cancellationToken) ?? throw new Exception(id.ToString());
        if (!HasViewPermissionForPage(page))
        {
            throw new Exception("you are not authorized to view this page");
        }

        return page;
    }

    private bool HasViewPermissionForPage(Page page)
    {
        if (!page.ViewRoleIds.IsNullOrEmpty())
        {
            // page is restricted
            var userHasAccess = page.ViewRoleIds.Any(r => _applicationContext.Current.IsInRole(r));
            var userIsPageAdmin = page.AdminRoleIds.Any(r => _applicationContext.Current.IsInRole(r));
            var userIsSiteAdmin = _applicationContext.Current.Site.AdminRoleIds.Any(r => _applicationContext.Current.IsInRole(r));
            var userIsSuperAdmin = _applicationContext.Current.IsSuperAdmin;
            if (!userHasAccess && !userIsPageAdmin && !userIsSiteAdmin && !userIsSuperAdmin)
            {
                return false;
            }
        }
        return true;
    }

    public async Task<Page> Create(Page page, CancellationToken cancellationToken = default)
    {
        // TODO: check permissions, only admins can create a page
        // Except for the first site, which is created by the system
        CheckAdminPermission();

        // normalizing the page path to lowercase
        NormalizePath(page);
        await CheckForDuplicatePath(page);

        page.CreatedBy = _applicationContext.Current?.User?.UserName ?? string.Empty;
        page.LastUpdatedBy = _applicationContext.Current?.User?.UserName ?? string.Empty;

        var newPage = await _pageRepository.Create(page, cancellationToken);
        return newPage ?? throw new Exception("Page not created");
    }

    private void CheckAdminPermission()
    {
        var userIsSiteAdmin = _applicationContext.Current.Site.AdminRoleIds.Any(r => _applicationContext.Current.IsInRole(r));
        var userIsSuperAdmin = _applicationContext.Current.IsSuperAdmin;
        if (!userIsSiteAdmin && !userIsSuperAdmin)
        {
            throw new Exception("only admins can create a page");
        }
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
        return newPage ?? throw new Exception("Page not created");
    }

    private void CheckPageOrAdminPermission(Page page)
    {
        var isSuperAdmin = _applicationContext.Current.IsSuperAdmin;
        var isAdmin = !_applicationContext.Current.Site.AdminRoleIds.Any(r => _applicationContext.Current.IsInRole(r));
        var isPageAdmin = _applicationContext.Current.IsInRole(page.AdminRoleIds);
        if (!isSuperAdmin && !isAdmin && !isPageAdmin)
        {
            throw new Exception("only super-admins, site-admins and page-admins can update or delete a page");
        }
    }

    private static void NormalizePath(Page page)
    {
        page.Path = page.Path.Trim().Replace(" ", "-").ToLower();
    }

    public Task Delete(Page page, CancellationToken cancellationToken = default)
    {
        CheckPageOrAdminPermission(page);
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

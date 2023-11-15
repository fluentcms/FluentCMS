using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using Microsoft.IdentityModel.Tokens;

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
        // Block users with roles lower than "SiteAdmin" from creating a page
        if (!Current.IsSiteAdmin)
            throw new AppPermissionException();

        PrepareForCreate(page);

        // this could be an async validation if we're using FluentValidator
        await BlockDuplicatePath(page);

        return await _pageRepository.Create(page, cancellationToken) ?? throw new AppException(ExceptionCodes.PageUnableToCreate);
    }

    public async Task Delete(Page page, CancellationToken cancellationToken = default)
    {
        // make sure that we are checking permissions from database, not the data that user has sent
        var previousPage = await _pageRepository.GetById(page.Id, cancellationToken) ?? throw new AppException(ExceptionCodes.PageNotFound);

        // block users with roles lower than "PageAdmin" from deleting a page
        if (IsPageAdmin(previousPage))
            throw new AppPermissionException();

        _ = await _pageRepository.Delete(page.Id, cancellationToken) ?? throw new AppException(ExceptionCodes.PageUnableToDelete);
    }


    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await _pageRepository.GetById(id, cancellationToken) ?? throw new AppException(ExceptionCodes.PageNotFound);

        // check if the page has restricted view roles, and if the user is not in those roles, and user is not a site admin
        if (!page.ViewRoleIds.IsNullOrEmpty() &&
            !Current.IsInRole(page.ViewRoleIds) &&
            !Current.IsSiteAdmin)
        {
            throw new AppPermissionException();
        }

        return page;
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        var pages = await _pageRepository.GetBySiteId(siteId, cancellationToken) ?? throw new AppException(ExceptionCodes.PageNotFound);

        // filter pages based on the user roles
        // the page does not have a view role, or if the user is in those roles, or user is a site admin
        return pages.Where(x => !x.ViewRoleIds.IsNullOrEmpty() || Current.IsInRole(x.ViewRoleIds) || Current.IsSiteAdmin);
    }

    public async Task<Page> Update(Page page, CancellationToken cancellationToken = default)
    {
        // make sure that we are checking permissions from database, not the data that user has sent
        var previousPage = await _pageRepository.GetById(page.Id, cancellationToken) ?? throw new AppException(ExceptionCodes.PageNotFound);

        if (!IsPageAdmin(previousPage))
            throw new AppPermissionException();

        // this could be an async validation if we're using FluentValidator
        await BlockDuplicatePath(page);

        PrepareForUpdate(page);

        return await _pageRepository.Update(page, cancellationToken) ?? throw new AppException(ExceptionCodes.HostUnableToUpdate);
    }

    private async Task BlockDuplicatePath(Page page)
    {
        // find a page with the same path
        var matchingPath = await _pageRepository.GetByPath(page.Path);

        // if there's a page with the same path and it's not the page we're updating
        if (matchingPath != null && matchingPath.Id != page.Id)
        {
            throw new AppException(ExceptionCodes.PagePathMustBeUnique);
        }
    }

    private bool IsPageAdmin(Page page)
    {
        return
            Current.IsSiteAdmin ||
            Current.IsInRole(page.AdminRoleIds);
    }
}

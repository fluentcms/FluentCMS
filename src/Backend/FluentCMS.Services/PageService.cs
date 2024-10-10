using System.Text.RegularExpressions;

namespace FluentCMS.Services;

public interface IPageService : IAutoRegisterService
{
    Task<IEnumerable<PageModel>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<PageModel> GetByFullPath(Guid siteId, string fullPath, CancellationToken cancellationToken = default);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
    Task<Page> Update(Page page, CancellationToken cancellationToken = default);
    Task<Page> Delete(Guid id, CancellationToken cancellationToken = default);
}

public class PageService(IPageRepository pageRepository, ISiteRepository siteRepository, IPageInternalService internalService, IMessagePublisher messagePublisher, IPermissionManager permissionManager) : IPageService
{
    public async Task<Page> Create(Page page, CancellationToken cancellationToken = default)
    {
        var site = (await siteRepository.GetById(page.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        if (!await permissionManager.HasAccess(site.Id, SitePermissionAction.SiteContributor, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        ValidateAndNormalize(page);

        await ValidateParentPage(page, cancellationToken);

        var newPage = await pageRepository.Create(page, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageUnableToCreate);

        // Invalidate cache for the site after creating a page
        internalService.Invalidate(page.SiteId);

        await messagePublisher.Publish(new Message<Page>(ActionNames.PageCreated, newPage), cancellationToken);

        return newPage;
    }

    public async Task<Page> Update(Page page, CancellationToken cancellationToken = default)
    {
        //fetch original page from db
        var originalPage = await pageRepository.GetById(page.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        var site = (await siteRepository.GetById(originalPage.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        //if (!await permissionManager.HasAccess(site.Id, page.Id, PagePermissionAction.PageAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        // site id cannot be changed
        page.SiteId = originalPage.SiteId;

        ValidateAndNormalize(page);
        await ValidateParentPage(page, cancellationToken);

        //// Only validate url if user wants to change path
        //if (page.Path != originalPage.Path)
        //    ValidateUrl(page, pages);

        var updatedPage = await pageRepository.Update(page, cancellationToken)
             ?? throw new AppException(ExceptionCodes.PageUnableToUpdate);

        // Invalidate cache for the site after updating a page
        internalService.Invalidate(page.SiteId);

        await messagePublisher.Publish(new Message<Page>(ActionNames.PageUpdated, updatedPage), cancellationToken);

        return updatedPage;
    }

    public async Task<Page> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var originalPage = await internalService.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        //if (!await permissionManager.HasAccess(originalPage.SiteId, originalPage.Id, PagePermissionAction.PageAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        // fetch site
        var site = (await siteRepository.GetById(originalPage.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        // check that it does not have any children
        if (originalPage.Children.Count != 0)
            throw new AppException(ExceptionCodes.PageHasChildren);

        var deletedPage = await pageRepository.Delete(id, cancellationToken) ??
             throw new AppException(ExceptionCodes.PageUnableToDelete);

        // Invalidate cache for the site after deleting a page
        internalService.Invalidate(originalPage.SiteId);

        await messagePublisher.Publish(new Message<Page>(ActionNames.PageDeleted, deletedPage), cancellationToken);

        return deletedPage;
    }

    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        var page = await internalService.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        //if (!await permissionManager.HasAccess(page, PermissionActionNames.PageView, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        return page;
    }

    public async Task<IEnumerable<PageModel>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        var pagesDict = await internalService.GetAllBySiteId(siteId, cancellationToken);
        return pagesDict.Values.ToList();
    }

    public async Task<PageModel> GetByFullPath(Guid siteId, string fullPath, CancellationToken cancellationToken = default)
    {
        var page = await internalService.GetByFullPath(siteId, fullPath, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        return page;
    }

    #region Private Methods

    // Private helper method to normalize the path
    private static string NormalizeFullPath(string path)
    {
        // Normalize the page path
        path = path.ToLowerInvariant().Trim();

        // If the path has at the end forward slash (/), remove it
        path = path.TrimEnd('/');

        // check if path does not start with forward slash (/), add it
        if (!path.StartsWith("/"))
            path = "/" + path;

        return path;
    }

    // Helper to validate site and permission
    private static void ValidateAndNormalize(Page page)
    {
        page.Path = NormalizeFullPath(page.Path);

        // Check if path is valid 
        // Only one segment without forward slash (/) is allowed
        // that should starts with forward slash (/)
        // Alphanumeric characters (only lowercase) : a-z, 0-9
        // Special characters: -, _, ., ~
        if (!Regex.IsMatch(page.Path, @"^\/[a-z0-9-_~]+$") && page.Path != "/")
            throw new AppException(ExceptionCodes.PagePathInvalidCharacter);

    }

    private async Task ValidateParentPage(Page page, CancellationToken cancellationToken = default)
    {
        // If Parent Id is assigned
        if (page.ParentId != null)
        {
            // Fetch pages beforehand to avoid multiple db calls
            var parentPage = await pageRepository.GetById(page.ParentId.Value, cancellationToken) ??
                throw new AppException(ExceptionCodes.PageParentPageNotFound);

            // If parent id is not on the same site
            if (parentPage.SiteId != page.SiteId)
                throw new AppException(ExceptionCodes.PageParentMustBeOnTheSameSite);
        }
    }

    #endregion
}

using FluentCMS.Providers.MessageBusProviders;

namespace FluentCMS.Services;

public interface IPageService : IAutoRegisterService
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Page> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
    Task<Page> Update(Page page, CancellationToken cancellationToken = default);
    Task<Page> Delete(Guid id, CancellationToken cancellationToken = default);
}

public class PageService(IPageRepository pageRepository, ISiteRepository siteRepository, IMessagePublisher messagePublisher, IPermissionManager permissionManager) : IPageService
{

    public async Task<Page> Create(Page page, CancellationToken cancellationToken = default)
    {
        var site = (await siteRepository.GetById(page.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        if (!await permissionManager.HasAccess(site.Id, SitePermissionAction.SiteContributor, cancellationToken))
            throw new AppException(ExceptionCodes.PermissionDenied);

        // If Parent Id is assigned
        if (page.ParentId != null)
        {
            // Fetch pages beforehand to avoid multiple db calls
            var pages = (await pageRepository.GetAll(cancellationToken)).ToList();
            ValidateParentPage(page, pages);
        }

        //// fetch list of all pages
        //ValidateUrl(page, pages);

        var newPage = await pageRepository.Create(page, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageUnableToCreate);

        await messagePublisher.Publish(new Message<Page>(ActionNames.PageCreated, newPage), cancellationToken);

        return newPage;
    }

    public async Task<Page> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch original page from db
        var originalPage = await pageRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        //if (!await permissionManager.HasAccess(originalPage.SiteId, originalPage.Id, PagePermissionAction.PageAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        // fetch site
        var site = (await siteRepository.GetById(originalPage.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        // check that it does not have any children
        var pages = (await pageRepository.GetAll(cancellationToken)).ToList();
        if (pages.Any(x => x.ParentId == id && x.SiteId == originalPage.SiteId))
            throw new AppException(ExceptionCodes.PageHasChildren);

        var deletedPage = await pageRepository.Delete(id, cancellationToken) ??
             throw new AppException(ExceptionCodes.PageUnableToDelete);

        await messagePublisher.Publish(new Message<Page>(ActionNames.PageDeleted, deletedPage), cancellationToken);

        return deletedPage;
    }

    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        var page = await pageRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        //if (!await permissionManager.HasAccess(page, PermissionActionNames.PageView, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        return page;
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        // fetch pages from db
        var sitePages = await pageRepository.GetAllForSite(siteId, cancellationToken);

        //var pages = await permissionManager.HasAccess(sitePages, PermissionActionNames.PageView, cancellationToken);

        return sitePages;
    }

    public async Task<Page> Update(Page page, CancellationToken cancellationToken = default)
    {

        //fetch original page from db
        var originalPage = await pageRepository.GetById(page.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        //if (!await permissionManager.HasAccess(originalPage.SiteId, originalPage.Id, PagePermissionAction.PageAdmin, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        // site id cannot be changed
        if (originalPage.SiteId != page.SiteId)
            throw new AppException(ExceptionCodes.PageSiteIdCannotBeChanged);

        // fetch list of all pages
        var pages = (await pageRepository.GetAll(cancellationToken)).ToList();

        //If Parent Id is changed validate again
        if (page.ParentId != originalPage.ParentId)
        {
            //validate parent
            ValidateParentPage(page, pages);
        }

        // Only validate url if user wants to change path
        if (page.Path != originalPage.Path)
            ValidateUrl(page, pages);

        var updatedPage = await pageRepository.Update(page, cancellationToken)
             ?? throw new AppException(ExceptionCodes.PageUnableToUpdate);

        await messagePublisher.Publish(new Message<Page>(ActionNames.PageUpdated, updatedPage), cancellationToken);

        return updatedPage;
    }

    public async Task<Page> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        var pages = (await pageRepository.GetAll(cancellationToken)).ToList();

        var page = pages.Where(x => x.SiteId == siteId && x.Path.ToLowerInvariant() == path.ToLowerInvariant()).SingleOrDefault() ??
            throw new AppException(ExceptionCodes.PageNotFound);

        //if (!await permissionManager.HasAccess(page, PermissionActionNames.PageView, cancellationToken))
        //    throw new AppException(ExceptionCodes.PermissionDenied);

        return page;
    }

    #region Private Methods

    private static void ValidateUrl(Page page, List<Page> pages)
    {
        //urls can be cached in a dictionary to avoid multiple list traversal
        var cachedUrls = new Dictionary<Guid, string>();

        // Build Url based on parent
        var fullUrl = BuildFullPath(page, pages, cachedUrls);
        var fullPaths = pages.Select(x => BuildFullPath(x, pages, cachedUrls));
        // Check if url is unique
        if (fullPaths.Any(x => x.Equals(fullUrl)))
            throw new AppException(ExceptionCodes.PagePathMustBeUnique);

    }

    private static string BuildFullPath(Page page, IEnumerable<Page> pages, Dictionary<Guid, string> cachedUrls)
    {
        //Traverse the pages to root (ParentId == null) and keep them in an array
        var parents = new List<string> { page.Path };
        var parentId = page.ParentId;

        while (parentId != null)
        {
            if (cachedUrls.ContainsKey(parentId.Value))
            {
                parents.Add(cachedUrls[parentId.Value]);
                parentId = null;
                continue;
            }
            var parent = pages.Single(x => x.Id == parentId);
            parents.Add(parent.Path);
            cachedUrls[parent.Id] = parent.Path;
            parentId = parent.ParentId;
        }
        //Build Path string from List of Parents
        return string.Join("/", parents.Reverse<string>());
    }

    private void ValidateParentPage(Page page, List<Page> pages)
    {
        var parent = pages.SingleOrDefault(x => x.Id == page.ParentId);

        // If parent id is not a valid page id
        if (parent is null)
            throw new AppException(ExceptionCodes.PageParentPageNotFound);

        // If parent id is not on the same site
        if (parent.SiteId != page.SiteId)
            throw new AppException(ExceptionCodes.PageParentMustBeOnTheSameSite);

        // if page viewRoles are a subset of parent view roles
        //if (!page.ViewRoleIds.ToImmutableHashSet().IsSubsetOf(parent.ViewRoleIds))
        //    throw new AppException(ExceptionCodes.PageViewPermissionsAreNotASubsetOfParent);
    }

    #endregion
}

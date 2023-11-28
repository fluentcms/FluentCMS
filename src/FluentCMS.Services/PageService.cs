﻿using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IPageService
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Page> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
    Task<Page> Update(Page page, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
}

public class PageService(
    IPageRepository pageRepository,
    ISiteRepository siteRepository,
    SitePolicies sitePolicies,
    IAuthorizationProvider authorizationProvider) : IPageService
{

    public async Task<Page> Create(Page page, CancellationToken cancellationToken = default)
    {
        // Check if site id exists
        var site = (await siteRepository.GetById(page.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        // check if user is site admin 
        if (!authorizationProvider.Authorize(site, sitePolicies.Admin))
            throw new AppPermissionException();

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

        // add admin permission to the page 
        await authorizationProvider.Create(newPage, Policies.PAGE_ADMIN, cancellationToken);

        // add edit permission to the page
        await authorizationProvider.Create(newPage, Policies.PAGE_EDITOR, cancellationToken);

        return newPage;
    }

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

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch original page from db
        var originalPage = await pageRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        // fetch site
        var site = (await siteRepository.GetById(originalPage.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        // check if user is siteAdmin, superAdmin or PageAdmin
        if (!HasAdminPermission(site, originalPage))
            throw new AppPermissionException();

        // check that it does not have any children
        var pages = (await pageRepository.GetAll(cancellationToken)).ToList();
        if (pages.Any(x => x.ParentId == id && x.SiteId == originalPage.SiteId))
            throw new AppException(ExceptionCodes.PageHasChildren);

        await pageRepository.Delete(id, cancellationToken);
    }

    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        var page = await pageRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        // fetch site
        var site = (await siteRepository.GetById(page.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        // check current user has permission to view page or is site admin or page admin
        return HasViewPermission(site, page) ? page : throw new AppPermissionException();
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        //fetch site from db
        var site = await siteRepository.GetById(siteId, cancellationToken) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        // fetch pages from db
        var pages = await pageRepository.GetBySiteId(siteId, cancellationToken);

        // if current user is page viewer or page admin or site admin
        return pages.Where(page => HasViewPermission(site, page));
    }

    public async Task<Page> Update(Page page, CancellationToken cancellationToken = default)
    {
        //fetch original page from db
        var originalPage = await pageRepository.GetById(page.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        // Check if site id exists
        var site = (await siteRepository.GetById(page.SiteId, cancellationToken)) ??
            throw new AppException(ExceptionCodes.SiteNotFound);

        // check if user is siteAdmin or superAdmin or pageAdmin
        if (!HasAdminPermission(site, originalPage))
            throw new AppPermissionException();

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

            //TODO: we should validate children permissions too!
        }

        ValidateUrl(page, pages);

        return await pageRepository.Update(page, cancellationToken)
            ?? throw new AppException(ExceptionCodes.PageUnableToUpdate);
    }

    public async Task<Page> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default)
    {
        var page = await pageRepository.GetByPath(siteId, path, cancellationToken) ??
            throw new AppException(ExceptionCodes.PageNotFound);

        return page;
    }

    private bool HasAdminPermission(Site site, Page page)
    {
        return true; // Current.IsInRole(site.AdminRoleIds) || Current.IsInRole(page.AdminRoleIds);
    }

    private bool HasViewPermission(Site site, Page page)
    {
        return true;// HasAdminPermission(site, page) || Current.IsInRole(page.ViewRoleIds);
    }
}

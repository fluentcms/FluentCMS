using AutoMapper;
using FluentCMS.Providers.CacheProviders;

namespace FluentCMS.Services;

public interface IPageInternalService : IAutoRegisterService
{
    Task<IEnumerable<PageModel>> GetHierarchyBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, PageModel>> GetAllBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<PageModel?> GetByFullPath(Guid siteId, string fullPath, CancellationToken cancellationToken = default);
    Task<PageModel?> GetById(Guid pageId, CancellationToken cancellationToken = default);
    void Invalidate(Guid siteId);
}

public class PageInternalService(IPageRepository pageRepository, IMapper mapper, ICacheProvider cacheProvider) : IPageInternalService
{
    public async Task<Dictionary<Guid, PageModel>> GetAllBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetAllPagesDictionaryCacheKey(siteId);

        // Try to get from cache first
        if (cacheProvider.TryGetValue(cacheKey, out Dictionary<Guid, PageModel>? cachedPages))
        {
            return cachedPages!;
        }

        // If not cached, get from repository
        var allPages = await pageRepository.GetAllForSite(siteId, cancellationToken);

        // Build dictionary model
        var pageModels = BuildPagesDictionary(allPages);

        // Cache the result
        cacheProvider.Set(cacheKey, pageModels);

        return pageModels;
    }

    public async Task<PageModel?> GetByFullPath(Guid siteId, string fullPath, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetFullPathCacheKey(siteId);

        var pagesDictionary = await GetAllBySiteId(siteId, cancellationToken);

        // Try to get from cache first
        if (!cacheProvider.TryGetValue(cacheKey, out Dictionary<string, Guid>? cachedFullPath))
        {
            cachedFullPath = pagesDictionary.ToDictionary(x => x.Value.FullPath, x => x.Value.Id);

            // Cache the result
            cacheProvider.Set(cacheKey, cachedFullPath);
        }

        if (cachedFullPath!.TryGetValue(fullPath, out Guid pageId))
            return pagesDictionary[pageId];
        else
            return null;
    }

    public async Task<IEnumerable<PageModel>> GetHierarchyBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetAllPagesCacheKey(siteId);

        // Try to get from cache first
        if (cacheProvider.TryGetValue(cacheKey, out IEnumerable<PageModel>? cachedPages))
        {
            return cachedPages!;
        }

        // If not cached, get from internal cached dictionary
        var pageDictionary = await GetAllBySiteId(siteId, cancellationToken);

        // only return the root pages
        var pageModels = pageDictionary.Values.Where(p => !p.ParentId.HasValue).OrderBy(x => x.Order);

        // Cache the result
        cacheProvider.Set(cacheKey, pageModels);

        return pageModels;

    }

    public async Task<PageModel?> GetById(Guid pageId, CancellationToken cancellationToken = default)
    {
        var pages = await GetAllBySiteId(pageId, cancellationToken);

        if (pages.TryGetValue(pageId, out PageModel? page))
            return page;
        else
            return null;
    }

    public void Invalidate(Guid siteId)
    {
        cacheProvider.Remove(GetAllPagesCacheKey(siteId));
        cacheProvider.Remove(GetAllPagesDictionaryCacheKey(siteId));
        cacheProvider.Remove(GetFullPathCacheKey(siteId));
    }

    #region Private

    private static string GetAllPagesCacheKey(Guid siteId) => $"site_all_pages_{siteId}";
    private static string GetAllPagesDictionaryCacheKey(Guid siteId) => $"site_all_pages_dictionary_{siteId}";
    private static string GetFullPathCacheKey(Guid siteId) => $"site_full-path-guid_dictionary_{siteId}";

    // Helper method to build hierarchical page model
    private Dictionary<Guid, PageModel> BuildPagesDictionary(IEnumerable<Page> pages)
    {
        // Similar to the original implementation to construct the page hierarchy
        var pageDictionary = pages.ToDictionary(p => p.Id, p =>
        {
            var model = mapper.Map<PageModel>(p);
            model.FullPath = p.Path;
            return model;
        });

        foreach (var page in pages)
        {
            if (page.ParentId.HasValue)
            {
                var parentPage = pageDictionary[page.ParentId.Value];
                var childPage = pageDictionary[page.Id];

                childPage.FullPath = $"{parentPage.FullPath}{childPage.FullPath}";
                parentPage.Children.Add(childPage);
            }
        }

        // apply order to children
        foreach (var page in pageDictionary.Values)
        {
            page.Children = [.. page.Children.OrderBy(x => x.Order)];
        }

        return pageDictionary;
    }

    #endregion
}

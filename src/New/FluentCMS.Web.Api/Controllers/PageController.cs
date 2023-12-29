using FluentCMS.Web.Api.Filters;

namespace FluentCMS.Web.Api.Controllers;

public class PageController(
    ISiteService siteService,
    IPageService pageService,
    IPluginDefinitionService pluginDefinitionService,
    IPluginService pluginService,
    ILayoutService layoutService,
    IMapper mapper) : BaseGlobalController
{

    [HttpGet("{siteUrl}")]
    [DecodeQueryParam]
    public async Task<IApiPagingResult<PageDetailResponse>> GetAll([FromRoute] string siteUrl, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetByUrl(siteUrl, cancellationToken);
        var entities = await pageService.GetBySiteId(site.Id, cancellationToken);
        var entitiesResponse = mapper.Map<List<PageDetailResponse>>(entities.ToList());
        return OkPaged(entitiesResponse);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<PageDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await pageService.GetById(id, cancellationToken);
        var entityResponse = mapper.Map<PageDetailResponse>(entity);
        return Ok(entityResponse);
    }

    [HttpGet("{siteUrl}/{path}")]
    [DecodeQueryParam]
    public async Task<IApiResult<PageFullDetailResponse>> GetByPath([FromRoute] string siteUrl, [FromRoute] string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
            path = "/";

        if (!path.StartsWith("/"))
            path = "/" + path;

        var site = await siteService.GetByUrl(siteUrl, cancellationToken);
        var pages = (await pageService.GetBySiteId(site.Id, cancellationToken)).ToDictionary(x => x.Id);
        var layouts = await layoutService.GetAll(site.Id, cancellationToken);
        var pluginDefinitions = (await pluginDefinitionService.GetAll(cancellationToken)).ToDictionary(x => x.Id);

        // defining a dictionary of nested paths (full paths) to page ids
        var fullPaths = GetFullPaths(pages);

        if (!fullPaths.ContainsKey(path))
            throw new AppException(ExceptionCodes.PageNotFound);

        var page = pages[fullPaths[path]];

        var plugins = await pluginService.GetByPageId(page.Id, cancellationToken);

        var pageResponse = mapper.Map<PageFullDetailResponse>(page);
        pageResponse.FullPath = path;
        pageResponse.Site = mapper.Map<SiteDetailResponse>(site);

        if (page.LayoutId.HasValue)
        {
            var layout = layouts.Where(l => l.Id == page.LayoutId.Value).First();
            pageResponse.Layout = mapper.Map<LayoutDetailResponse>(layout);
        }
        else
        {
            var layout = layouts.Where(l => l.IsDefault).First();
            pageResponse.Layout = mapper.Map<LayoutDetailResponse>(layout);
        }

        foreach (var plugin in plugins)
        {
            if (!pageResponse.Sections.ContainsKey(plugin.Section))
                pageResponse.Sections.Add(plugin.Section, []);

            var pluginResponse = mapper.Map<PluginDetailResponse>(plugin);
            pluginResponse.Definition = mapper.Map<PluginDefinitionDetailResponse>(pluginDefinitions[plugin.DefinitionId]);
            pageResponse.Sections[plugin.Section].Add(pluginResponse);
        }

        return Ok(pageResponse);
    }

    [HttpPost]
    public async Task<IApiResult<PageDetailResponse>> Create(PageCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Page>(request);
        var newEntity = await pageService.Create(entity, cancellationToken);
        var pageResponse = mapper.Map<PageDetailResponse>(newEntity);
        return Ok(pageResponse);
    }

    [HttpPut]
    public async Task<IApiResult<PageDetailResponse>> Update(PageUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Page>(request);
        var updatedEntity = await pageService.Update(entity, cancellationToken);
        var entityResponse = mapper.Map<PageDetailResponse>(updatedEntity);
        return Ok(entityResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        await pageService.Delete(id);
        return Ok(true);
    }

    #region Private Methods
    // defining a dictionary of nested paths (full paths) to page ids
    private static Dictionary<string, Guid> GetFullPaths(Dictionary<Guid, Page> pages)
    {
        var fullPaths = new Dictionary<string, Guid>();
        foreach (var page in pages.Values)
        {
            fullPaths.Add(GetFullPath(pages, page.Id), page.Id);
        }
        return fullPaths;
    }

    // this function will return a full path for a page based on its nested parent
    private static string GetFullPath(Dictionary<Guid, Page> pages, Guid pageId)
    {
        var page = pages[pageId];
        var path = page.Path;
        if (page.ParentId.HasValue)
        {
            var parentPage = pages[page.ParentId.Value];
            path = GetFullPath(pages, parentPage.Id) + path;
        }

        return path;
    }
    #endregion
}

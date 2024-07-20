using FluentCMS.Web.Api.Filters;
using FluentCMS.Web.Api.Setup;

namespace FluentCMS.Web.Api.Controllers;

public class PageController(
    ISiteService siteService,
    IPageService pageService,
    IPluginDefinitionService pluginDefinitionService,
    IPluginService pluginService,
    ILayoutService layoutService,
    ISetupManager setupManager,
    IMapper mapper) : BaseGlobalController
{

    public const string AREA = "Page Management";
    public const string UPDATE = $"Update";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete";

    [HttpGet("{siteUrl}")]
    [DecodeQueryParam]
    [PolicyAll]
    public async Task<IApiPagingResult<PageDetailResponse>> GetAll([FromRoute] string siteUrl, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetByUrl(siteUrl, cancellationToken);
        var entities = await pageService.GetBySiteId(site.Id, cancellationToken);
        var entitiesResponse = mapper.Map<List<PageDetailResponse>>(entities.ToList());
        return OkPaged(entitiesResponse);
    }

    [HttpGet("{id}")]
    [PolicyAll]
    public async Task<IApiResult<PageDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await pageService.GetById(id, cancellationToken);
        var entityResponse = mapper.Map<PageDetailResponse>(entity);
        return Ok(entityResponse);
    }

    [HttpGet]
    [DecodeQueryParam]
    [PolicyAll]
    public async Task<IApiResult<PageFullDetailResponse>> GetByUrl([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(url);
        var domain = uri.Authority;
        var path = uri.AbsolutePath;

        var initialized = await setupManager.IsInitialized();
        if (!initialized)
            return Ok(await setupManager.GetSetupPage());

        return await GetPageResponse(domain, path, cancellationToken);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<PageDetailResponse>> Create(PageCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Page>(request);
        var newEntity = await pageService.Create(entity, cancellationToken);
        var pageResponse = mapper.Map<PageDetailResponse>(newEntity);
        return Ok(pageResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<PageSectionDetailResponse>> CreateSection(PageSectionCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<PageSection>(request);
        var newEntity = await pageService.CreateSection(entity);
        var response = mapper.Map<PageSectionDetailResponse>(newEntity);
        return Ok(response);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<PageRowDetailResponse>> CreateRow(PageRowCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<PageRow>(request);
        var newEntity = await pageService.CreateRow(entity);
        var response = mapper.Map<PageRowDetailResponse>(newEntity);
        return Ok(response);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<PageColumnDetailResponse>> CreateColumn(PageColumnCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<PageColumn>(request);
        var newEntity = await pageService.CreateColumn(entity);
        var response = mapper.Map<PageColumnDetailResponse>(newEntity);
        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<PageDetailResponse>> Update(PageUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<Page>(request);
        var updatedEntity = await pageService.Update(entity, cancellationToken);
        var entityResponse = mapper.Map<PageDetailResponse>(updatedEntity);
        return Ok(entityResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<PageColumnDetailResponse>> UpdateColumn(PageColumnUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<PageColumn>(request);
        var updatedEntity = await pageService.UpdateColumn(entity, cancellationToken);
        var entityResponse = mapper.Map<PageColumnDetailResponse>(updatedEntity);
        return Ok(entityResponse);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id)
    {
        await pageService.Delete(id);
        return Ok(true);
    }

    #region Private Methods

    private static string GetFullPath(Dictionary<Guid, Page> allPages, Page page)
    {
        // append parents' path to the current page's path recursively
        // until there is no parent, then return the full path. The separator should be a slash.
        var currentPath = !page.Path.StartsWith("/") ? "/" + page.Path : page.Path;
        return page.ParentId.HasValue ? GetFullPath(allPages, allPages[page.ParentId.Value]) + currentPath : currentPath;
    }

    private async Task<IApiResult<PageFullDetailResponse>> GetPageResponse(string domain, string path, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetByUrl(domain, cancellationToken);
        var pages = (await pageService.GetBySiteId(site.Id, cancellationToken)).ToDictionary(x => x.Id, x => x);
        var pluginDefinitions = (await pluginDefinitionService.GetAll(cancellationToken)).ToDictionary(x => x.Id);

        var pagesByPath = new Dictionary<string, Page>();

        foreach (var _page in pages.Values)
        {
            pagesByPath.Add(GetFullPath(pages, _page), _page);
        }

        if (!pagesByPath.TryGetValue(path, out Page? page))
            throw new AppException(ExceptionCodes.PageNotFound);

        var layoutId = page.LayoutId ?? site.LayoutId;
        var layout = await layoutService.GetById(layoutId, cancellationToken);
        // var plugins = await pluginService.GetByPageId(page.Id, cancellationToken);
        var sections = await pageService.GetSectionsByPageId(page.Id, cancellationToken);
        var mappedSections = mapper.Map<List<PageSectionDetailResponse>>(sections);

        
        var pageResponse = mapper.Map<PageFullDetailResponse>(page);
        pageResponse.Site = mapper.Map<SiteDetailResponse>(site);
        pageResponse.Layout = mapper.Map<LayoutDetailResponse>(layout);
        pageResponse.Sections = mappedSections;
        pageResponse.FullPath = path;

        foreach (var section in mappedSections)
        {
            var rows = await pageService.GetRowsBySectionId(section.Id, cancellationToken);
            var mappedRows = mapper.Map<List<PageRowDetailResponse>>(rows);

            foreach (var row in mappedRows)
            {
                var columns = await pageService.GetColumnsByRowId(row.Id, cancellationToken);
                var mappedColumns = mapper.Map<List<PageColumnDetailResponse>>(columns);

                foreach (var column in mappedColumns)
                {
                    var plugins = await pluginService.GetByColumnId(column.Id);
                    var mappedPlugins = mapper.Map<List<PluginDetailResponse>>(plugins);
                    
                    foreach (var plugin in mappedPlugins)
                    {
                        // Definition
                        plugin.Definition = mapper.Map<PluginDefinitionDetailResponse>(await pluginDefinitionService.GetById(plugin.DefinitionId));
                    }

                    column.Plugins = mappedPlugins;
                }
                row.Columns = mappedColumns;
            }
            section.Rows = mappedRows;
        }
        
        return Ok(pageResponse);
    }

    #endregion
}

using FluentCMS.Web.Api.Filters;
using FluentCMS.Web.Api.Setup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace FluentCMS.Web.Api.Controllers;

[AllowAnonymous]
public class PageController(
    ISiteService siteService,
    IPageService pageService,
    IPluginDefinitionService pluginDefinitionService,
    IPluginService pluginService,
    ILayoutService layoutService,
    SetupManager setupManager,
    IMapper mapper) : BaseGlobalController
{
    public const string PLUGIN_DEFINIOTION_NAME = "PluginDef";

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

    [HttpGet]
    [DecodeQueryParam]
    public async Task<IApiResult<PageFullDetailResponse>> GetByUrl([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(url);
        var domain = uri.Authority;
        var path = uri.AbsolutePath;
        var query = QueryHelpers.ParseQuery(uri.Query);

        var initialized = await setupManager.IsInitialized();
        if (!initialized)
            return Ok(await setupManager.GetSetupPage());

        if (query.TryGetValue(PLUGIN_DEFINIOTION_NAME, out _))
            return await GetDynamicPage(domain, path, query, cancellationToken);
        else
            return await GetRealPage(domain, path, cancellationToken);
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
    private async Task<IApiResult<PageFullDetailResponse>> GetRealPage(string domain, string path, CancellationToken cancellationToken = default)
    {
        var site = await siteService.GetByUrl(domain, cancellationToken);
        var pages = (await pageService.GetBySiteId(site.Id, cancellationToken)).ToDictionary(x => x.Id);
        var pluginDefinitions = (await pluginDefinitionService.GetAll(cancellationToken)).ToDictionary(x => x.Id);

        // defining a dictionary of nested paths (full paths) to page ids
        var fullPaths = GetFullPaths(pages);

        if (!fullPaths.TryGetValue(path, out Guid value))
            throw new AppException(ExceptionCodes.PageNotFound);

        var page = pages[value];

        var plugins = await pluginService.GetByPageId(page.Id, cancellationToken);

        var pageResponse = mapper.Map<PageFullDetailResponse>(page);
        pageResponse.FullPath = path;
        pageResponse.Site = mapper.Map<SiteDetailResponse>(site);

        var layoutId = page.LayoutId ?? site.LayoutId;
        var layout = await layoutService.GetById(layoutId, cancellationToken);
        pageResponse.Layout = mapper.Map<LayoutDetailResponse>(layout);

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

    private async Task<IApiResult<PageFullDetailResponse>> GetDynamicPage(string domain, string path, Dictionary<string, StringValues> query, CancellationToken cancellationToken = default)
    {
        // example.com?pluginDef=pluginDefName&typeName=pluginDefTypeName&layout=layoutName
        var site = await siteService.GetByUrl(domain, cancellationToken);
        var pages = (await pageService.GetBySiteId(site.Id, cancellationToken)).ToDictionary(x => x.Id);
        var pluginDefinitions = (await pluginDefinitionService.GetAll(cancellationToken)).ToList();

        var pluginDefinitionName = query[PLUGIN_DEFINIOTION_NAME].FirstOrDefault() ??
            throw new Exception("Plugin Definition is not set!");

        var pluginDefinition = pluginDefinitions.Where(p => p.Name.Equals(query[PLUGIN_DEFINIOTION_NAME].ToString(), StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
        //?? throw new Exception("Plugin Definition not found!");

        // defining a dictionary of nested paths (full paths) to page ids
        var fullPaths = GetFullPaths(pages);

        Page page;
        if (fullPaths.TryGetValue(path, out Guid value))
            page = pages[value];
        else
            page = new Page { Path = path, SiteId = site.Id, Title = pluginDefinition.Name };

        var pageResponse = mapper.Map<PageFullDetailResponse>(page);
        pageResponse.FullPath = path;
        pageResponse.Site = mapper.Map<SiteDetailResponse>(site);

        var layoutId = page.LayoutId ?? site.LayoutId;
        var layout = await layoutService.GetById(layoutId, cancellationToken);
        pageResponse.Layout = mapper.Map<LayoutDetailResponse>(layout);

        pageResponse.Sections = [];
        var pluginResponse = mapper.Map<PluginDetailResponse>(GetRuntimePlugin(site.Id, page.Id, pluginDefinition.Id));
        pluginResponse.Definition = mapper.Map<PluginDefinitionDetailResponse>(pluginDefinition);
        pageResponse.Sections.Add("Main", [pluginResponse]);

        return Ok(pageResponse);
    }

    private Plugin GetRuntimePlugin(Guid siteId, Guid pageId, Guid pluginDefId)
    {
        return new Plugin
        {
            PageId = pageId,
            Section = "Main",
            Order = 0,
            SiteId = siteId,
            DefinitionId = pluginDefId
        };
    }
    #endregion
}

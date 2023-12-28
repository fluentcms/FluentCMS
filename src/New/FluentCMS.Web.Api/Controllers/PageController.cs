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
        var page = await pageService.GetByPath(site.Id, path, cancellationToken);
        var layouts = await layoutService.GetAll(site.Id, cancellationToken);
        var pluginDefinitions = (await pluginDefinitionService.GetAll(cancellationToken)).ToDictionary(x => x.Id);
        var plugins = await pluginService.GetByPageId(page.Id, cancellationToken);

        var pageResponse = mapper.Map<PageFullDetailResponse>(page);
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
}

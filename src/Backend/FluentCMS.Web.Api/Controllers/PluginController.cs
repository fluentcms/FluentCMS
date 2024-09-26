namespace FluentCMS.Web.Api.Controllers;

public class PluginController(IPluginService pluginService, IPluginDefinitionService pluginDefinitionService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Plugin Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet("{pageId}")]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<PluginDetailResponse>> GetByPageId([FromRoute] Guid pageId, CancellationToken cancellationToken = default)
    {
        var plugins = await pluginService.GetByPageId(pageId, cancellationToken);
        var pluginsResponse = mapper.Map<List<PluginDetailResponse>>(plugins);
        return OkPaged(pluginsResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<PluginDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.GetById(id, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(plugin);
        return Ok(response);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<PluginDetailResponse>> InitialCreate([FromBody] PluginInitialCreateRequest request, CancellationToken cancellationToken = default)
    {
        var newPlugin = await pluginService.InitialCreate(request.PageId, request.DefinitionId, request.Section, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(newPlugin);
        var pluginDefinition = await pluginDefinitionService.GetById(request.DefinitionId, cancellationToken);
        response.Definition = mapper.Map<PluginDefinitionDetailResponse>(pluginDefinition);
        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiPagingResult<PluginDetailResponse>> UpdateOrders(PluginUpdateOrdersRequest request, CancellationToken cancellationToken = default)
    {
        var plugins = await pluginService.UpdateOrders(request.PluginOrders, cancellationToken);
        var results = mapper.Map<List<PluginDetailResponse>>(plugins);
        var pluginDefinitions = await pluginDefinitionService.GetAll(cancellationToken);
        foreach (var pluginResponse in results)
        {
            pluginResponse.Definition = mapper.Map<PluginDefinitionDetailResponse>(pluginDefinitions.Single(x => x.Id == pluginResponse.DefinitionId));
        }
        return OkPaged(results);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<PluginDetailResponse>> UpdateCols([FromBody] PluginUpdateColsRequest request, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.GetById(request.Id, cancellationToken);
        plugin.Cols = request.Cols;
        plugin.ColsMd = request.ColsMd;
        plugin.ColsLg = request.ColsLg;

        var updated = await pluginService.Update(plugin, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(updated);
        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<PluginDetailResponse>> UpdateSettings([FromBody] PluginUpdateSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.GetById(request.Id, cancellationToken);
        plugin.Settings = request.Settings;

        var updated = await pluginService.Update(plugin, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await pluginService.Delete(id, cancellationToken);
        return Ok(true);
    }
}

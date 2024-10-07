namespace FluentCMS.Web.Api.Controllers;

public class PluginController(IPluginService pluginService, IPluginDefinitionService pluginDefinitionService, ISettingsService settingsService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Plugin Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<PluginDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.GetById(id, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(plugin);
        await SetPluginDefinition(response, cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<PluginDetailResponse>> InitialCreate([FromBody] PluginInitialCreateRequest request, CancellationToken cancellationToken = default)
    {
        var newPlugin = await pluginService.InitialCreate(request.PageId, request.DefinitionId, request.Section, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(newPlugin);
        await SetPluginDefinition(response, cancellationToken);
        await SetPluginSettings(response, cancellationToken);
        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiPagingResult<PluginDetailResponse>> UpdateOrders(PluginUpdateOrdersRequest request, CancellationToken cancellationToken = default)
    {
        var plugins = await pluginService.UpdateOrders(request.PluginOrders, cancellationToken);
        var results = mapper.Map<List<PluginDetailResponse>>(plugins);
        await SetPluginDefinitions(results, cancellationToken);
        await SetPluginsSettings(results, cancellationToken);
        return OkPaged(results);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<PluginDetailResponse>> UpdateCols([FromBody] PluginUpdateColsRequest request, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.UpdateCols(request.Id, request.Cols, request.ColsMd, request.ColsLg, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(plugin);
        await SetPluginDefinition(response, cancellationToken);
        await SetPluginSettings(response, cancellationToken);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await pluginService.Delete(id, cancellationToken);
        return Ok(true);
    }

    private async Task SetPluginDefinition(PluginDetailResponse response, CancellationToken cancellationToken)
    {
        var pluginDefinition = await pluginDefinitionService.GetById(response.DefinitionId, cancellationToken);
        response.Definition = mapper.Map<PluginDefinitionDetailResponse>(pluginDefinition);
    }

    private async Task SetPluginDefinitions(IEnumerable<PluginDetailResponse> responses, CancellationToken cancellationToken)
    {
        var pluginDefinitions = await pluginDefinitionService.GetAll(cancellationToken);
        foreach (var response in responses)
            response.Definition = mapper.Map<PluginDefinitionDetailResponse>(pluginDefinitions.Single(x => x.Id == response.DefinitionId));
    }

    private async Task SetPluginSettings(PluginDetailResponse response, CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetById(response.Id, cancellationToken);
        response.Settings = settings.Values;
    }

    private async Task SetPluginsSettings(IEnumerable<PluginDetailResponse> responses, CancellationToken cancellationToken)
    {
        var settingsDict = (await settingsService.GetByIds(responses.Select(x => x.Id), cancellationToken)).ToDictionary(x => x.Id, x => x.Values);
        foreach (var response in responses)
            if (settingsDict.TryGetValue(response.Id, out Dictionary<string, string>? value))
                response.Settings = value;
    }

}

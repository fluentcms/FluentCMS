namespace FluentCMS.Web.Api.Controllers;

public class PluginController(IPluginService pluginService, IMapper mapper) : BaseGlobalController
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
    public async Task<IApiResult<PluginDetailResponse>> Create([FromBody] PluginCreateRequest request, CancellationToken cancellationToken = default)
    {
        var plugin = mapper.Map<Plugin>(request);
        var newPlugin = await pluginService.Create(plugin, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(newPlugin);
        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<PluginDetailResponse>> Update([FromBody] PluginUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.GetById(request.Id, cancellationToken);
        plugin.Order = request.Order;
        plugin.Cols = request.Cols;
        plugin.ColsMd = request.ColsMd;
        plugin.ColsLg = request.ColsLg;
        plugin.Section = request.Section;
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

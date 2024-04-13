using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

public class PluginController(IPluginService pluginService, IMapper mapper) : BaseGlobalController
{
    [HttpGet("{pageId}")]
    [AllowAnonymous]
    public async Task<IApiPagingResult<PluginDetailResponse>> GetByPageId([FromRoute] Guid pageId, CancellationToken cancellationToken = default)
    {
        var plugins = await pluginService.GetByPageId(pageId, cancellationToken);
        var pluginsResponse = mapper.Map<List<PluginDetailResponse>>(plugins);
        return OkPaged(pluginsResponse);
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IApiResult<PluginDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.GetById(id, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(plugin);
        return Ok(response);
    }

    [HttpPost]
    public async Task<IApiResult<PluginDetailResponse>> Create([FromBody] PluginCreateRequest request, CancellationToken cancellationToken = default)
    {
        var plugin = mapper.Map<Plugin>(request);
        var newPlugin = await pluginService.Create(plugin, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(newPlugin);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IApiResult<PluginDetailResponse>> Update([FromBody] PluginUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var plugin = mapper.Map<Plugin>(request);
        var updated = await pluginService.Update(plugin, cancellationToken);
        var response = mapper.Map<PluginDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await pluginService.Delete(id, cancellationToken);
        return Ok(true);
    }
}

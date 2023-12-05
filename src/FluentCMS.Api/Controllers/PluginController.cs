using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PluginController(IPluginService pluginService, IMapper mapper) : BaseController
{

    [HttpGet("[action]/{pageId}")]
    public async Task<IApiPagingResult<Plugin>> GetByPageId([FromRoute] Guid pageId, CancellationToken cancellationToken = default)
    {
        var plugins = await pluginService.GetByPageId(pageId, cancellationToken);
        return new ApiPagingResult<Plugin>(plugins);
    }

    [HttpGet("[action]/{id}")]
    public async Task<IApiResult<Plugin>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.GetById(id, cancellationToken);
        return new ApiResult<Plugin>(plugin);
    }

    [HttpPost]
    public async Task<IApiResult<Plugin>> Post([FromBody] Plugin plugin, CancellationToken cancellationToken = default)
    {
        var result = await pluginService.Create(plugin, cancellationToken);
        return new ApiResult<Plugin>(result);
    }

    [HttpPut("{id}")]
    public async Task<IApiResult<Plugin>> Put([FromBody] Plugin plugin, CancellationToken cancellationToken = default)
    {
        var result = await pluginService.Update(plugin, cancellationToken);
        return new ApiResult<Plugin>(result);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await pluginService.Delete(id, cancellationToken);
        return new ApiResult<bool>(true);
    }
}

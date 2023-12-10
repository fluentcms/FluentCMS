using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

[ApiController]
[Route("api/[controller]/{contentType}/[action]")]
[Produces("application/json")]
public class PluginContentController(IPluginContentService pluginContentService)
{
    [HttpPost]
    public async Task<IApiResult<PluginContent>> Create([FromRoute] string contentType, PluginContent content, CancellationToken cancellationToken = default)
    {
        content.Type = contentType;
        var newContent = await pluginContentService.Create(content, cancellationToken);
        return new ApiResult<PluginContent>(newContent);
    }

    [HttpPut]
    public async Task<IApiResult<PluginContent>> Update([FromRoute] string contentType, PluginContent content, CancellationToken cancellationToken = default)
    {
        content.Type = contentType;
        var updatedContent = await pluginContentService.Update(content, cancellationToken);
        return new ApiResult<PluginContent>(updatedContent);
    }

    [HttpDelete]
    public async Task<BooleanResponse> Delete([FromRoute] string contentType, IdRequest request, CancellationToken cancellationToken = default)
    {
        await pluginContentService.Delete(request.SiteId, contentType, request.Id, cancellationToken);
        return new BooleanResponse(true);
    }

    [HttpGet]
    public async Task<IApiPagingResult<PluginContent>> GetByPluginId([FromRoute] string contentType, [FromQuery] Guid siteId, [FromQuery] Guid pluginId, CancellationToken cancellationToken = default)
    {
        var contents = await pluginContentService.GetByPluginId(siteId, contentType, pluginId, cancellationToken);
        return new ApiPagingResult<PluginContent>(contents);
    }

}

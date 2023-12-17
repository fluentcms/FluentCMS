using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// API controller for managing plugin content entities in the FluentCMS system.
/// Provides actions to create, update, delete, and retrieve plugin content data.
/// </summary>
[ApiController]
[Route("api/[controller]/{contentType}/[action]")]
[Produces("application/json")]
public class PluginContentController(IPluginContentService pluginContentService)
{
    /// <summary>
    /// Creates a new plugin content entity in the system.
    /// </summary>
    /// <param name="contentType">The type of content associated with the plugin.</param>
    /// <param name="request">The plugin content creation request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The created plugin content entity.</returns>
    [HttpPost]
    public async Task<IApiResult<PluginContent>> Create([FromRoute] string contentType, PluginContentCreateRequest request, CancellationToken cancellationToken = default)
    {
        var pluginContent = new PluginContent
        {
            PluginId = request.PluginId,
            SiteId = request.SiteId,
            Type = contentType,
            Value = request.Value
        };
        var newContent = await pluginContentService.Create(pluginContent, cancellationToken);
        return new ApiResult<PluginContent>(newContent);
    }

    /// <summary>
    /// Updates an existing plugin content entity in the system.
    /// </summary>
    /// <param name="contentType">The type of content associated with the plugin.</param>
    /// <param name="request">The plugin content update request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The updated plugin content entity.</returns>
    [HttpPut]
    public async Task<IApiResult<PluginContent>> Update([FromRoute] string contentType, [FromBody] PluginContentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var pluginContent = new PluginContent
        {
            Id = request.Id,
            PluginId = request.PluginId,
            SiteId = request.SiteId,
            Type = contentType,
            Value = request.Value
        };
        var updatedContent = await pluginContentService.Update(pluginContent, cancellationToken);
        return new ApiResult<PluginContent>(updatedContent);
    }

    [HttpDelete("{id}")]
    public async Task<ApiResult<bool>> Delete([FromRoute] string contentType, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await pluginContentService.Delete(contentType, id, cancellationToken);
        return new ApiResult<bool>(true);
    }

    [HttpGet("{pluginId}")]
    public async Task<IApiPagingResult<PluginContent>> GetByPluginId([FromRoute] string contentType, [FromQuery] Guid pluginId, CancellationToken cancellationToken = default)
    {
        var contents = await pluginContentService.GetByPluginId(contentType, pluginId, cancellationToken);
        return new ApiPagingResult<PluginContent>(contents);
    }

    [HttpGet("{siteId}")]
    public async Task<IApiPagingResult<Content>> GetAll([FromRoute] string contentType, [FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var contents = await pluginContentService.GetAll(contentType, siteId, cancellationToken);
        return new ApiPagingResult<Content>(contents);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<Content>> GetById([FromRoute] string contentType, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var content = await pluginContentService.GetById(contentType, id, cancellationToken);
        return new ApiResult<Content>(content);
    }
}

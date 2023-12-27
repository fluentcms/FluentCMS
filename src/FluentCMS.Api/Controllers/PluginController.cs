using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// API controller for managing plugin entities in the FluentCMS system.
/// Provides actions to create, update, delete, and retrieve plugin data.
/// </summary>
public class PluginController(IPluginService pluginService) : BaseController
{
    /// <summary>
    /// Retrieves plugin entities associated with a specific page.
    /// </summary>
    /// <param name="pageId">The unique identifier of the page for which plugins are to be retrieved.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A paginated result containing plugins associated with the specified page.</returns>
    [HttpGet("{pageId}")]
    public async Task<IApiPagingResult<Plugin>> GetByPageId([FromRoute] Guid pageId, CancellationToken cancellationToken = default)
    {
        var plugins = await pluginService.GetByPageId(pageId, cancellationToken);
        return new ApiPagingResult<Plugin>(plugins);
    }

    /// <summary>
    /// Retrieves a specific plugin entity by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the plugin to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The requested plugin entity.</returns>
    [HttpGet("{id}")]
    public async Task<IApiResult<Plugin>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var plugin = await pluginService.GetById(id, cancellationToken);
        return new ApiResult<Plugin>(plugin);
    }

    /// <summary>
    /// Creates a new plugin entity in the system.
    /// </summary>
    /// <param name="plugin">The plugin entity to create.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The created plugin entity.</returns>
    [HttpPost]
    public async Task<IApiResult<Plugin>> Create([FromBody] Plugin plugin, CancellationToken cancellationToken = default)
    {
        var result = await pluginService.Create(plugin, cancellationToken);
        return new ApiResult<Plugin>(result);
    }

    /// <summary>
    /// Updates an existing plugin entity in the system.
    /// </summary>
    /// <param name="plugin">The plugin entity with updated information.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The updated plugin entity.</returns>
    [HttpPut]
    public async Task<IApiResult<Plugin>> Update([FromBody] Plugin plugin, CancellationToken cancellationToken = default)
    {
        var result = await pluginService.Update(plugin, cancellationToken);
        return new ApiResult<Plugin>(result);
    }

    /// <summary>
    /// Deletes a plugin entity from the system.
    /// </summary>
    /// <param name="id">The unique identifier of the plugin to delete.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A boolean result indicating the success of the operation.</returns>
    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await pluginService.Delete(id, cancellationToken);
        return new ApiResult<bool>(true);
    }
}

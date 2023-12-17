using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// API controller for managing content entities in the FluentCMS system.
/// Provides actions to create, update, delete, and retrieve content data.
/// </summary>
/// <remarks>
/// Routes are defined with content type as part of the URL to allow for content-specific operations.
/// </remarks>
[ApiController]
[Route("api/[controller]/{contentType}/[action]")]
[Produces("application/json")]
public class ContentController(IContentService<Content> contentService)
{
    /// <summary>
    /// Creates a new content entity in the system.
    /// </summary>
    /// <param name="contentType">The type of content to create.</param>
    /// <param name="request">The content creation request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The created content entity.</returns>
    [HttpPost]
    public async Task<IApiResult<Content>> Create([FromRoute] string contentType, [FromBody] ContentCreateRequest request, CancellationToken cancellationToken = default)
    {
        var content = new Content
        {
            SiteId = request.SiteId,
            Value = request.Value,
            Type = contentType
        };

        var newContent = await contentService.Create(content, cancellationToken);

        return new ApiResult<Content>(newContent);
    }

    /// <summary>
    /// Updates an existing content entity in the system.
    /// </summary>
    /// <param name="contentType">The type of content to update.</param>
    /// <param name="request">The content update request data.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The updated content entity.</returns>
    [HttpPut]
    public async Task<IApiResult<Content>> Update([FromRoute] string contentType, [FromBody] ContentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var content = new Content
        {
            Id = request.Id,
            SiteId = request.SiteId,
            Value = request.Value,
            Type = contentType
        };

        var updatedContent = await contentService.Update(content, cancellationToken);

        return new ApiResult<Content>(updatedContent);
    }

    /// <summary>
    /// Deletes a content entity from the system.
    /// </summary>
    /// <param name="contentType">The type of content to delete.</param>
    /// <param name="id">The unique identifier of the content to delete.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A boolean result indicating the success of the operation.</returns>
    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string contentType, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await contentService.Delete(contentType, id, cancellationToken);
        return new ApiResult<bool>(true);
    }

    /// <summary>
    /// Retrieves all content entities of a specified type associated with a given site.
    /// </summary>
    /// <param name="contentType">The type of content to retrieve.</param>
    /// <param name="siteId">The unique identifier of the site for which content is to be retrieved.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An enumeration of content entities.</returns>
    [HttpGet("{siteId}")]
    public async Task<IApiPagingResult<Content>> GetAll([FromRoute] string contentType, [FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var contents = await contentService.GetAll(contentType, siteId, cancellationToken);
        return new ApiPagingResult<Content>(contents);
    }

    /// <summary>
    /// Retrieves a specific content entity by its identifier.
    /// </summary>
    /// <param name="contentType">The type of content to retrieve.</param>
    /// <param name="id">The unique identifier of the content to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The requested content entity.</returns>
    [HttpGet("{id}")]
    public async Task<IApiResult<Content>> GetById([FromRoute] string contentType, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var content = await contentService.GetById(contentType, id, cancellationToken);
        return new ApiResult<Content>(content);
    }
}

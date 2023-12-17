using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// ContentController manages content-related operations for different content types.
/// </summary>
[ApiController]
[Route("api/[controller]/{contentType}/[action]")]
[Produces("application/json")]
public class ContentController(IContentService<Content> contentService)
{
    /// <summary>
    /// Creates a new content item of a specified content type.
    /// </summary>
    /// <param name="contentType">The type of the content to be created.</param>
    /// <param name="content">The content data for creation.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The details of the newly created content item.</returns>
    [HttpPost]
    public async Task<IApiResult<Content>> Create([FromRoute] string contentType, Content content, CancellationToken cancellationToken = default)
    {
        content.Type = contentType;
        var newContent = await contentService.Create(content, cancellationToken);
        return new ApiResult<Content>(newContent);
    }

    /// <summary>
    /// Updates an existing content item of a specified content type.
    /// </summary>
    /// <param name="contentType">The type of the content to be updated.</param>
    /// <param name="content">The content data for update.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The details of the updated content item.</returns>
    [HttpPut]
    public async Task<IApiResult<Content>> Update([FromRoute] string contentType, Content content, CancellationToken cancellationToken = default)
    {
        content.Type = contentType;
        var updatedContent = await contentService.Update(content, cancellationToken);
        return new ApiResult<Content>(updatedContent);
    }

    /// <summary>
    /// Deletes a specific content item of a given content type.
    /// </summary>
    /// <param name="contentType">The type of the content to be deleted.</param>
    /// <param name="siteId">The site ID where the content is located.</param>
    /// <param name="id">The unique identifier of the content to be deleted.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>A boolean value indicating whether the deletion was successful.</returns>
    [HttpDelete("{siteId}/{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string contentType, [FromRoute] Guid id, [FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        await contentService.Delete(siteId, contentType, id, cancellationToken);
        return new ApiResult<bool>(true);
    }

    /// <summary>
    /// Retrieves all content items of a specific content type associated with a site.
    /// </summary>
    /// <param name="contentType">The type of the content to retrieve.</param>
    /// <param name="siteId">The site ID to filter the content items.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>A paginated list of content items of the specified type for the given site.</returns>
    [HttpGet("{siteId}")]
    public async Task<IApiPagingResult<Content>> GetAll([FromRoute] string contentType, [FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var contents = await contentService.GetAll(siteId, contentType, cancellationToken);
        return new ApiPagingResult<Content>(contents);
    }

    /// <summary>
    /// Retrieves a specific content item of a given content type by its identifier.
    /// </summary>
    /// <param name="contentType">The type of the content to retrieve.</param>
    /// <param name="siteId">The site ID where the content is located.</param>
    /// <param name="id">The unique identifier of the content item to retrieve.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The content item details if found, or null.</returns>
    [HttpGet("{siteId}/{id}")]
    public async Task<IApiResult<Content>> GetById([FromRoute] string contentType, [FromRoute] Guid id, [FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var content = await contentService.GetById(siteId, contentType, id, cancellationToken);
        return new ApiResult<Content>(content);
    }
}

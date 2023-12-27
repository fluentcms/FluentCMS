using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

/// <summary>
/// ContentTypeController manages operations related to content types.
/// </summary>
public class ContentTypeController(IContentTypeService contentTypeService) : BaseController
{
    /// <summary>
    /// Retrieves all content types.
    /// </summary>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>A paginated list of content types.</returns>
    [HttpGet]
    public async Task<IApiPagingResult<ContentType>> GetAll(CancellationToken cancellationToken = default)
    {
        var contentTypes = await contentTypeService.GetAll(cancellationToken);
        return new ApiPagingResult<ContentType>(contentTypes.ToList());
    }

    /// <summary>
    /// Retrieves a specific content type by its identifier.
    /// </summary>
    /// <param name="id">The unique identifier of the content type.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The content type details if found, or null.</returns>
    [HttpGet("{id}")]
    public async Task<IApiResult<ContentType>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeService.GetById(id, cancellationToken);
        return new ApiResult<ContentType>(contentType);
    }

    /// <summary>
    /// Retrieves a specific content type by its name.
    /// </summary>
    /// <param name="name">The name of the content type.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The content type details if found, or null.</returns>
    [HttpGet("{name}")]
    public async Task<IApiResult<ContentType>> GetByName([FromRoute] string name, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeService.GetByName(name, cancellationToken);
        return new ApiResult<ContentType>(contentType);
    }

    /// <summary>
    /// Creates a new content type.
    /// </summary>
    /// <param name="request">The content type creation request containing the content type details.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The details of the newly created content type.</returns>
    [HttpPost]
    public async Task<IApiResult<ContentType>> Create(ContentType request, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeService.Create(request, cancellationToken);
        return new ApiResult<ContentType>(contentType);
    }

    /// <summary>
    /// Updates an existing content type.
    /// </summary>
    /// <param name="request">The content type update request containing the updated content type details.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The details of the updated content type.</returns>
    [HttpPut]
    public async Task<IApiResult<ContentType>> Update(ContentType request, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.Update(request, cancellationToken);
        return new ApiResult<ContentType>(updated);
    }

    /// <summary>
    /// Deletes a specific content type.
    /// </summary>
    /// <param name="id">The unique identifier of the content type to be deleted.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>A boolean value indicating whether the deletion was successful.</returns>
    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await contentTypeService.Delete(id, cancellationToken);
        return new ApiResult<bool>(true);
    }

    /// <summary>
    /// Sets a field for a specific content type.
    /// </summary>
    /// <param name="id">The unique identifier of the content type.</param>
    /// <param name="request">The content type field setting request.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The details of the updated content type.</returns>
    [HttpPut("{id}")]
    public async Task<IApiResult<ContentType>> SetField([FromRoute] Guid id, ContentTypeField request, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.SetField(id, request, cancellationToken);
        return new ApiResult<ContentType>(updated);
    }

    /// <summary>
    /// Deletes a field from a specific content type.
    /// </summary>
    /// <param name="id">The unique identifier of the content type.</param>
    /// <param name="name">The name of the field to be deleted.</param>
    /// <param name="cancellationToken">A token for canceling the request.</param>
    /// <returns>The details of the content type after deleting the field.</returns>
    [HttpDelete("{id}/{name}")]
    public async Task<IApiResult<ContentType>> DeleteField([FromRoute] Guid id, [FromRoute] string name, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.RemoveField(id, name, cancellationToken);
        return new ApiResult<ContentType>(updated);
    }
}

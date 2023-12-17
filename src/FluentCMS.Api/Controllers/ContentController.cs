using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

[ApiController]
[Route("api/[controller]/{contentType}/[action]")]
[Produces("application/json")]
public class ContentController(IContentService<Content> contentService)
{
    [HttpPost]
    public async Task<IApiResult<Content>> Create([FromRoute] string contentType, Content content, CancellationToken cancellationToken = default)
    {
        content.Type = contentType;
        var newContent = await contentService.Create(content, cancellationToken);
        return new ApiResult<Content>(newContent);
    }

    [HttpPut]
    public async Task<IApiResult<Content>> Update([FromRoute] string contentType, Content content, CancellationToken cancellationToken = default)
    {
        content.Type = contentType;
        var updatedContent = await contentService.Update(content, cancellationToken);
        return new ApiResult<Content>(updatedContent);
    }

    [HttpDelete]
    public async Task<IApiResult<bool>> Delete([FromRoute] string contentType, [FromQuery] Guid siteId, [FromQuery] Guid id, CancellationToken cancellationToken = default)
    {
        await contentService.Delete(siteId, contentType, id, cancellationToken);
        return new ApiResult<bool>(true);
    }

    [HttpGet]
    public async Task<IApiPagingResult<Content>> GetAll([FromRoute] string contentType, [FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var contents = await contentService.GetAll(siteId, contentType, cancellationToken);
        return new ApiPagingResult<Content>(contents);
    }

    [HttpGet]
    public async Task<IApiResult<Content>> GetById([FromRoute] string contentType, [FromQuery] Guid siteId, [FromQuery] Guid id, CancellationToken cancellationToken = default)
    {
        var content = await contentService.GetById(siteId, contentType, id, cancellationToken);
        return new ApiResult<Content>(content);
    }
}

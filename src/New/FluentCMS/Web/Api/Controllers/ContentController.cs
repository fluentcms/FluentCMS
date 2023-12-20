using FluentCMS.Web.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[ApiController]
[Route("api/[controller]/{contentType}/[action]")]
[Produces("application/json")]
public class ContentController(IContentService<Content> contentService)
{

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

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string contentType, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await contentService.Delete(contentType, id, cancellationToken);
        return new ApiResult<bool>(true);
    }

    [HttpGet("{siteId}")]
    public async Task<IApiPagingResult<Content>> GetAll([FromRoute] string contentType, [FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var contents = await contentService.GetAll(contentType, siteId, cancellationToken);
        return new ApiPagingResult<Content>(contents);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<Content>> GetById([FromRoute] string contentType, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var content = await contentService.GetById(contentType, id, cancellationToken);
        return new ApiResult<Content>(content);
    }
}

using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[Route("{appSlug}/api/[controller]/{contentTypeSlug}/[action]")]
public class ContentController(IContentService<Content> contentService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<Content>> GetAll([FromRoute] string appSlug, [FromRoute] string contentTypeSlug, [FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var contents = await contentService.GetAll(contentType, siteId, cancellationToken);
        return new ApiPagingResult<Content>(contents);
    }

    [HttpPost]
    public async Task<IApiResult<Content>> Create([FromRoute] string appSlug, [FromRoute] string contentTypeSlug, [FromBody] ContentCreateRequest request, CancellationToken cancellationToken = default)
    {

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


}

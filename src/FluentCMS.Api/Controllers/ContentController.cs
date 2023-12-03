using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

[ApiController]
[Route("api/[controller]/{contentType}/[action]")]
[Produces("application/json")]
public class ContentController(IContentService contentService)
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
    public async Task<BooleanResponse> Delete([FromRoute] string contentType, IdRequest request, CancellationToken cancellationToken = default)
    {
        await contentService.Delete(contentType, request.Id, cancellationToken);
        return new BooleanResponse(true);
    }
}

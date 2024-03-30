﻿using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

[Authorize]

[Route("api/[controller]/{contentTypeSlug}/[action]")]
public class ContentController(
    IMapper mapper,
    IContentService contentService,
    IContentTypeService contentTypeService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<ContentDetailResponse>> GetAll([FromRoute] string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeService.GetBySlug(contentTypeSlug, cancellationToken);
        var contents = await contentService.GetAll(contentType.Id, cancellationToken);
        var contentResponses = mapper.Map<List<ContentDetailResponse>>(contents);
        return OkPaged(contentResponses);
    }

    [HttpPost]
    public async Task<IApiResult<ContentDetailResponse>> Create([FromRoute] string contentTypeSlug, [FromBody] ContentCreateRequest request, CancellationToken cancellationToken = default)
    {
        var content = mapper.Map<Content>(request);
        var contentType = await contentTypeService.GetBySlug(contentTypeSlug, cancellationToken);
        content.TypeId = contentType.Id;
        var newContent = await contentService.Create(content, cancellationToken);
        var response = mapper.Map<ContentDetailResponse>(newContent);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IApiResult<ContentDetailResponse>> Update([FromRoute] string contentTypeSlug, [FromBody] ContentUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var content = mapper.Map<Content>(request);
        var contentType = await contentTypeService.GetBySlug(contentTypeSlug, cancellationToken);
        content.TypeId = contentType.Id;
        var updated = await contentService.Update(content, cancellationToken);
        var response = mapper.Map<ContentDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string contentTypeSlug, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeService.GetBySlug(contentTypeSlug, cancellationToken);
        await contentService.Delete(contentType.Id, id, cancellationToken);
        return Ok(true);
    }


}

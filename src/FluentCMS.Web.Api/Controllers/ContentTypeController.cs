using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api.Controllers;

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ContentTypeController(IMapper mapper, IContentTypeService contentTypeService) : BaseGlobalController
{
    [HttpGet("{slug}")]
    public async Task<IApiResult<ContentTypeDetailResponse>> GetBySlug([FromRoute] string slug, CancellationToken cancellationToken = default)
    {
        var contentTypes = await contentTypeService.GetBySlug(slug, cancellationToken);
        var contentTypeResponses = mapper.Map<ContentTypeDetailResponse>(contentTypes);
        return Ok(contentTypeResponses);
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<ContentTypeDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var contentTypes = await contentTypeService.GetById(id, cancellationToken);
        var contentTypeResponses = mapper.Map<ContentTypeDetailResponse>(contentTypes);
        return Ok(contentTypeResponses);
    }

    [HttpGet]
    public async Task<IApiPagingResult<ContentTypeDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var contentTypes = await contentTypeService.GetAll(cancellationToken);
        var contentTypeResponses = mapper.Map<List<ContentTypeDetailResponse>>(contentTypes);
        return OkPaged(contentTypeResponses);
    }

    [HttpPost]
    public async Task<IApiResult<ContentTypeDetailResponse>> Create([FromBody] ContentTypeCreateRequest request, CancellationToken cancellationToken = default)
    {
        var contentType = mapper.Map<ContentType>(request);
        var newContentType = await contentTypeService.Create(contentType, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(newContentType);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IApiResult<ContentTypeDetailResponse>> Update([FromBody] ContentTypeUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var contentType = mapper.Map<ContentType>(request);
        var updated = await contentTypeService.Update(contentType, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await contentTypeService.Delete(id, cancellationToken);
        return Ok(true);
    }

    [HttpPut("{id}")]
    public async Task<IApiResult<ContentTypeDetailResponse>> SetField([FromRoute] Guid id, ContentTypeField request, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.SetField(id, request, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}/{name}")]
    public async Task<IApiResult<ContentTypeDetailResponse>> DeleteField([FromRoute] Guid id, [FromRoute] string name, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.DeleteField(id, name, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }
}

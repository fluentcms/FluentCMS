namespace FluentCMS.Web.Api.Controllers;

public class ContentTypeController(IMapper mapper, IContentTypeService contentTypeService) : BaseGlobalController
{
    public const string AREA = "Content Type Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet("{slug}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<ContentTypeDetailResponse>> GetBySlug([FromQuery] Guid siteId, [FromRoute] string slug, CancellationToken cancellationToken = default)
    {
        if (siteId == Guid.Empty)
            throw new AppException(ExceptionCodes.ContentTypeSiteIdRequired);

        var contentTypes = await contentTypeService.GetBySlug(siteId, slug, cancellationToken);
        var contentTypeResponses = mapper.Map<ContentTypeDetailResponse>(contentTypes);
        return Ok(contentTypeResponses);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<ContentTypeDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var contentTypes = await contentTypeService.GetById(id, cancellationToken);
        var contentTypeResponses = mapper.Map<ContentTypeDetailResponse>(contentTypes);
        return Ok(contentTypeResponses);
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<ContentTypeDetailResponse>> GetAll([FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var contentTypes = await contentTypeService.GetAll(siteId, cancellationToken);
        var contentTypeResponses = mapper.Map<List<ContentTypeDetailResponse>>(contentTypes);
        return OkPaged(contentTypeResponses);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<ContentTypeDetailResponse>> Create([FromBody] ContentTypeCreateRequest request, CancellationToken cancellationToken = default)
    {
        var contentType = mapper.Map<ContentType>(request);
        var newContentType = await contentTypeService.Create(contentType, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(newContentType);
        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<ContentTypeDetailResponse>> Update([FromBody] ContentTypeUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var contentType = mapper.Map<ContentType>(request);
        var updated = await contentTypeService.Update(contentType, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await contentTypeService.Delete(id, cancellationToken);
        return Ok(true);
    }

    [HttpPut("{id}")]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<ContentTypeDetailResponse>> SetField([FromRoute] Guid id, ContentTypeField request, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.SetField(id, request, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}/{name}")]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<ContentTypeDetailResponse>> DeleteField([FromRoute] Guid id, [FromRoute] string name, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.DeleteField(id, name, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }
}

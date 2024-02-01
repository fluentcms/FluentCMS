namespace FluentCMS.Web.Api.Controllers;

public class ContentTypeController(IMapper mapper, IContentTypeService contentTypeService, IAppService appService)
    : BaseAppController
{
    [HttpGet("{slug}")]
    public async Task<IApiPagingResult<ContentTypeDetailResponse>> GetBySlug([FromRoute] string slug, [FromRoute] string appSlug, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var contentTypes = await contentTypeService.GetBySlug(app.Id, slug, cancellationToken);
        var contentTypeResponses = mapper.Map<List<ContentTypeDetailResponse>>(contentTypes);
        return OkPaged(contentTypeResponses);
    }

    [HttpGet("{id}")]
    public async Task<IApiPagingResult<ContentTypeDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var contentTypes = await contentTypeService.GetById(id, cancellationToken);
        var contentTypeResponses = mapper.Map<List<ContentTypeDetailResponse>>(contentTypes);
        return OkPaged(contentTypeResponses);
    }

    [HttpGet]
    public async Task<IApiPagingResult<ContentTypeDetailResponse>> GetAll([FromRoute] string appSlug, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var contentTypes = await contentTypeService.GetAll(app.Id, cancellationToken);
        var contentTypeResponses = mapper.Map<List<ContentTypeDetailResponse>>(contentTypes);
        return OkPaged(contentTypeResponses);
    }

    [HttpPost]
    public async Task<IApiResult<ContentTypeDetailResponse>> Create([FromRoute] string appSlug, [FromBody] ContentTypeCreateRequest request, CancellationToken cancellationToken = default)
    {
        var contentType = mapper.Map<ContentType>(request);
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        contentType.AppId = app.Id;
        var newContentType = await contentTypeService.Create(contentType, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(newContentType);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IApiResult<ContentTypeDetailResponse>> Update([FromRoute] string appSlug, [FromBody] ContentTypeUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var contentType = mapper.Map<ContentType>(request);
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        contentType.AppId = app.Id;
        var updated = await contentTypeService.Update(contentType, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string appSlug, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        await contentTypeService.Delete(app.Id, id, cancellationToken);
        return Ok(true);
    }

    [HttpPut("{id}")]
    public async Task<IApiResult<ContentTypeDetailResponse>> SetField([FromRoute] string appSlug, [FromRoute] Guid id, ContentTypeField request, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var updated = await contentTypeService.SetField(app.Id, id, request, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}/{name}")]
    public async Task<IApiResult<ContentTypeDetailResponse>> DeleteField([FromRoute] string appSlug, [FromRoute] Guid id, [FromRoute] string name, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var updated = await contentTypeService.DeleteField(app.Id, id, name, cancellationToken);
        var response = mapper.Map<ContentTypeDetailResponse>(updated);
        return Ok(response);
    }
}

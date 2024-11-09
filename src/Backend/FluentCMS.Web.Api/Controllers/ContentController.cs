namespace FluentCMS.Web.Api.Controllers;

[Route("api/[controller]/{contentTypeSlug}/[action]")]
public class ContentController(
    IMapper mapper,
    IContentService contentService,
    IContentTypeService contentTypeService) : BaseController
{
    public const string AREA = "Content Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<ContentDetailResponse>> GetAll([FromQuery] Guid siteId, [FromRoute] string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        // TODO: Validate siteId
        var contentType = await contentTypeService.GetBySlug(siteId, contentTypeSlug, cancellationToken);
        var contents = await contentService.GetAll(contentType.Id, cancellationToken);
        var contentResponses = mapper.Map<List<ContentDetailResponse>>(contents);
        return OkPaged(contentResponses);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<ContentDetailResponse>> GetById([FromQuery] Guid siteId, [FromRoute] string contentTypeSlug, Guid id, CancellationToken cancellationToken = default)
    {
        // TODO: Validate siteId
        var contentType = await contentTypeService.GetBySlug(siteId, contentTypeSlug, cancellationToken);
        var content = await contentService.GetById(id, cancellationToken);
        var contentResponse = mapper.Map<ContentDetailResponse>(content);
        return Ok(contentResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<ContentDetailResponse>> Create([FromQuery] Guid siteId, [FromRoute] string contentTypeSlug, [FromBody] Dictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        // TODO: Validate siteId
        var contentType = await contentTypeService.GetBySlug(siteId, contentTypeSlug, cancellationToken);
        var content = new Content { 
            SiteId = siteId, 
            TypeId = contentType.Id, 
            Data = data 
        };
        await contentService.Create(content, cancellationToken);
        var response = mapper.Map<ContentDetailResponse>(content);
        return Ok(response);
    }

    [HttpPut("{id}")]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<ContentDetailResponse>> Update([FromQuery] Guid siteId, [FromRoute] string contentTypeSlug, [FromRoute] Guid id, [FromBody] Dictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        // TODO: Validate siteId
        var contentType = await contentTypeService.GetBySlug(siteId, contentTypeSlug, cancellationToken);
        var content = new Content { 
            Id = id, 
            SiteId = siteId,
            TypeId = contentType.Id, 
            Data = data 
        };
        await contentService.Update(content, cancellationToken);
        var response = mapper.Map<ContentDetailResponse>(content);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] string contentTypeSlug, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await contentService.Delete(id, cancellationToken);
        return Ok(true);
    }
}


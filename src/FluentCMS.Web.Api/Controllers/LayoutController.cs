using FluentCMS.Web.Api.Attributes;

namespace FluentCMS.Web.Api.Controllers;

public class LayoutController(IMapper mapper, ILayoutService layoutService) : BaseGlobalController
{
    public const string AREA = "Layout Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<LayoutDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var layouts = await layoutService.GetAll(cancellationToken);
        var LayoutsResponse = mapper.Map<IEnumerable<LayoutDetailResponse>>(layouts);
        return OkPaged(LayoutsResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<LayoutDetailResponse>> Get([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var layout = await layoutService.GetById(id, cancellationToken);
        var layoutResponse = mapper.Map<LayoutDetailResponse>(layout);
        return Ok(layoutResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<LayoutDetailResponse>> Create([FromBody] LayoutCreateRequest request, CancellationToken cancellationToken = default)
    {
        var layout = mapper.Map<Layout>(request);
        var newRole = await layoutService.Create(layout, cancellationToken);
        var layoutResponse = mapper.Map<LayoutDetailResponse>(newRole);
        return Ok(layoutResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<LayoutDetailResponse>> Update([FromBody] LayoutUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var layout = mapper.Map<Layout>(request);
        var updated = await layoutService.Update(layout, cancellationToken);
        var layoutResponse = mapper.Map<LayoutDetailResponse>(updated);
        return Ok(layoutResponse);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await layoutService.Delete(id, cancellationToken);
        return Ok(true);
    }
}

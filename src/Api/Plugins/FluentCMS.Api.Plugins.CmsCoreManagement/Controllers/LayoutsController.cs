namespace FluentCMS.Api.Plugins.CmsCoreManagement.Controllers;

public class LayoutsController(ILayoutService layoutService) : BaseController
{
    
    [HttpGet]
    public async Task<ApiListResponse<LayoutDto>> GetBySiteId([FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var layouts = await layoutService.GetBySiteId(siteId, cancellationToken);
        var layoutsDto = Mapper.Map<List<LayoutDto>>(layouts);
        return SuccessList(layoutsDto);
    }

    [HttpGet]
    public async Task<ApiListResponse<LayoutDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var layouts = await layoutService.GetAll(cancellationToken);
        var layoutsDto = Mapper.Map<List<LayoutDto>>(layouts);
        return SuccessList(layoutsDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<LayoutDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var layout = await layoutService.GetById(id, cancellationToken);
        return Success(Mapper.Map<LayoutDto>(layout));
    }

    [HttpPost]
    public async Task<ApiResponse<LayoutDto>> Add(LayoutAddRequest request, CancellationToken cancellationToken = default)
    {
        var layout = Mapper.Map<Layout>(request);
        await layoutService.Add(layout, cancellationToken);
        return Success(Mapper.Map<LayoutDto>(layout));
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<LayoutDto>> Update(Guid id, LayoutUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var layout = await layoutService.GetById(id, cancellationToken);
        Mapper.Map(request, layout);
        await layoutService.Update(layout, cancellationToken);
        return Success(Mapper.Map<LayoutDto>(layout));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        await layoutService.Remove(id, cancellationToken);
        return Success();
    }
}

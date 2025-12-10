namespace FluentCMS.Api.Plugins.CmsCoreManagement.Controllers;

public class BlocksController(IBlockService blockService) : BaseController
{
    
    [HttpGet]
    public async Task<ApiListResponse<BlockDto>> GetBySiteId([FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var blocks = await blockService.GetBySiteId(siteId, cancellationToken);
        
        var blocksDto = Mapper.Map<List<BlockDto>>(blocks);

        return SuccessList(blocksDto);
    }

    [HttpGet]
    public async Task<ApiListResponse<BlockDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var blocks = await blockService.GetAll(cancellationToken);
        var blocksDto = Mapper.Map<List<BlockDto>>(blocks);
        return SuccessList(blocksDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ApiResponse<BlockDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var block = await blockService.GetById(id, cancellationToken);
        return Success(Mapper.Map<BlockDto>(block));
    }

    [HttpPost]
    public async Task<ApiResponse<BlockDto>> Add(BlockAddRequest request, CancellationToken cancellationToken = default)
    {
        var block = Mapper.Map<Block>(request);
        await blockService.Add(block, cancellationToken);
        return Success(Mapper.Map<BlockDto>(block));
    }

    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<BlockDto>> Update(Guid id, BlockUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var block = await blockService.GetById(id, cancellationToken);
        Mapper.Map(request, block);
        await blockService.Update(block, cancellationToken);
        return Success(Mapper.Map<BlockDto>(block));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse> Remove(Guid id, CancellationToken cancellationToken = default)
    {
        await blockService.Remove(id, cancellationToken);
        return Success();
    }
}

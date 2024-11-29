namespace FluentCMS.Web.Api.Controllers;

public class BlockController(IMapper mapper, IBlockService blockService) : BaseGlobalController
{
    public const string AREA = "Block Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<BlockDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var blocks = await blockService.GetById(id, cancellationToken);
        var blockResponse = mapper.Map<BlockDetailResponse>(blocks);
        return Ok(blockResponse);
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<BlockDetailResponse>> GetAllForSite([FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var blocks = await blockService.GetAllForSite(siteId, cancellationToken);
        var blockResponse = mapper.Map<List<BlockDetailResponse>>(blocks);
        return OkPaged(blockResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<BlockDetailResponse>> Create([FromBody] BlockCreateRequest request, CancellationToken cancellationToken = default)
    {
        var block = mapper.Map<Block>(request);
        var newBlock = await blockService.Create(block, cancellationToken);
        var response = mapper.Map<BlockDetailResponse>(newBlock);
        return Ok(response);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<BlockDetailResponse>> Update([FromBody] BlockUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var block = mapper.Map<Block>(request);
        var updated = await blockService.Update(block, cancellationToken);
        var response = mapper.Map<BlockDetailResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await blockService.Delete(id, cancellationToken);
        return Ok(true);
    }
}

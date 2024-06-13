namespace FluentCMS.Web.Api.Controllers;

public class FolderController(IFolderService folderService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Asset Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<AssetDetailResponse>> GetAll([FromQuery] Guid? id, CancellationToken cancellationToken = default)
    {
        var childAssets = await folderService.GetByParentId(id, cancellationToken);
        var assetResponse = mapper.Map<List<AssetDetailResponse>>(childAssets);
        return OkPaged(assetResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<FolderDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.GetById(id, cancellationToken);
        var folderResponse = mapper.Map<FolderDetailResponse>(folder);
        var children = await folderService.GetByParentId(id, cancellationToken);
        folderResponse.Children = mapper.Map<List<AssetDetailResponse>>(children);
        return Ok(folderResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<FolderDetailResponse>> Create([FromBody] FolderCreateRequest request, CancellationToken cancellationToken = default)
    {
        var asset = await folderService.Create(request.Name, request.FolderId, cancellationToken);
        var assetResponse = mapper.Map<FolderDetailResponse>(asset);
        return Ok(assetResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<FolderDetailResponse>> Update([FromBody] FolderUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var asset = await folderService.Update(request.Id, request.Name, request.FolderId, cancellationToken);
        var assetResponse = mapper.Map<FolderDetailResponse>(asset);
        return Ok(assetResponse);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await folderService.Delete(id, cancellationToken);
        return Ok(true);
    }
}

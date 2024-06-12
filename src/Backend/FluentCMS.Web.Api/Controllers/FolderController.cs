namespace FluentCMS.Web.Api.Controllers;

public class FolderController(IFolderService folderService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Folder Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<FolderDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var folders = await folderService.GetAll(cancellationToken);
        var foldersResponse = mapper.Map<List<FolderDetailResponse>>(folders.ToList());
        return OkPaged(foldersResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<FolderDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.GetById(id, cancellationToken);
        var folderResponse = mapper.Map<FolderDetailResponse>(folder);
        return Ok(folderResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<FolderDetailResponse>> Create([FromBody] FolderCreateRequest request, CancellationToken cancellationToken = default)
    {
        var folder = mapper.Map<Folder>(request);
        await folderService.Create(folder, cancellationToken);
        var folderResponse = mapper.Map<FolderDetailResponse>(folder);
        return Ok(folderResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<FolderDetailResponse>> Update([FromBody] FolderUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var folder = mapper.Map<Folder>(request);
        await folderService.Update(folder, cancellationToken);
        var folderResponse = mapper.Map<FolderDetailResponse>(folder);
        return Ok(folderResponse);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await folderService.Delete(id, cancellationToken);
        return Ok(true);
    }
}

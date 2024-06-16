namespace FluentCMS.Web.Api.Controllers;

public class FolderController(IFolderService folderService, IFileService fileService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Asset Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    private static Folder _rootFolder = new() { Id = Guid.Empty, Name = "(root)", FolderId = Guid.Empty };

    [HttpGet]
    [PolicyAll]
    public async Task<IApiResult<FolderDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var folders = await folderService.GetAll(cancellationToken);
        var files = await fileService.GetAll(cancellationToken);

        var foldersResponseDict = folders.ToDictionary(x => x.Id, x => mapper.Map<FolderDetailResponse>(x));
        var filesResponseDict = files.ToDictionary(x => x.Id, x => mapper.Map<FileDetailResponse>(x));
        var rootFolderDetailResponse = mapper.Map<FolderDetailResponse>(_rootFolder);

        foldersResponseDict.Add(rootFolderDetailResponse.Id, rootFolderDetailResponse);

        foreach (var folderResponse in foldersResponseDict.Values)
        {
            folderResponse.Folders ??= [];
            folderResponse.Files ??= [];
            var parentFolderResponse = foldersResponseDict[folderResponse.FolderId];

            if (folderResponse.Id != Guid.Empty)
                parentFolderResponse.Folders.Add(folderResponse);
        }

        foreach (var fileResponse in filesResponseDict.Values)
        {
            var parentFolderResponse = foldersResponseDict[fileResponse.FolderId];
            parentFolderResponse.Files.Add(fileResponse);
        }

        return Ok(rootFolderDetailResponse);
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
        if (folder.FolderId == _rootFolder.Id)
            folder.FolderId = null;

        await folderService.Create(folder, cancellationToken);

        if (folder.FolderId == null)
            folder.FolderId = _rootFolder.Id;

        var folderResponse = mapper.Map<FolderDetailResponse>(folder);
        return Ok(folderResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<FolderDetailResponse>> Update([FromBody] FolderUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var folder = mapper.Map<Folder>(request);

        if (folder.FolderId == _rootFolder.Id)
            folder.FolderId = null;

        await folderService.Update(folder, cancellationToken);

        if (folder.FolderId == null)
            folder.FolderId = _rootFolder.Id;

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

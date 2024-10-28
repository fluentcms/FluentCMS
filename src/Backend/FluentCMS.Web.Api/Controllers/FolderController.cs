using FluentCMS.Web.Api.Models.Folder;

namespace FluentCMS.Web.Api.Controllers;

public class FolderController(IFolderService folderService, IFileService fileService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Folder Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet("{siteId}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<FolderDetailResponse>> GetAll([FromRoute] Guid siteId, CancellationToken cancellationToken = default)
    {
        var allFoldersDict = await GetFoldersResponseDict(siteId, cancellationToken);
        var rootFolderDetailResponse = allFoldersDict.Values.Single(x => x.ParentId == null);
        return Ok(rootFolderDetailResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<FolderDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.GetById(id, cancellationToken);
        var allFoldersDict = await GetFoldersResponseDict(folder.SiteId, cancellationToken);
        var folderDetailResponse = allFoldersDict[id];
        return Ok(folderDetailResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<FolderDetailResponse>> Create([FromBody] FolderCreateRequest request, CancellationToken cancellationToken = default)
    {
        var folder = mapper.Map<Folder>(request);

        await folderService.Create(folder, cancellationToken);

        var allFoldersDict = await GetFoldersResponseDict(folder.SiteId, cancellationToken);
        var folderDetailResponse = allFoldersDict[folder.Id];

        return Ok(folderDetailResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<FolderDetailResponse>> Rename([FromBody] FolderRenameRequest request, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.Rename(request.Id, request.Name, cancellationToken);

        var allFoldersDict = await GetFoldersResponseDict(folder.SiteId, cancellationToken);
        var folderDetailResponse = allFoldersDict[folder.Id];

        return Ok(folderDetailResponse);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<FolderDetailResponse>> Rename([FromBody] FolderMoveRequest request, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.Move(request.Id, request.ParentId, cancellationToken);

        var allFoldersDict = await GetFoldersResponseDict(folder.SiteId, cancellationToken);
        var folderDetailResponse = allFoldersDict[folder.Id];

        return Ok(folderDetailResponse);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await folderService.Delete(id, cancellationToken);
        return Ok(true);
    }

    private async Task<Dictionary<Guid, FolderDetailResponse>> GetFoldersResponseDict(Guid siteId, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, FolderDetailResponse>();
        var folders = await folderService.GetAll(siteId, cancellationToken);
        var rootFolder = folders.FirstOrDefault(x => x.ParentId == null);
        var files = await fileService.GetAll(siteId, cancellationToken);

        var foldersResponseDict = folders.ToDictionary(x => x.Id, mapper.Map<FolderDetailResponse>);
        var filesResponseDict = files.ToDictionary(x => x.Id, mapper.Map<FileDetailResponse>);
        var rootFolderDetailResponse = mapper.Map<FolderDetailResponse>(rootFolder);

        foldersResponseDict.Add(rootFolderDetailResponse.Id, rootFolderDetailResponse);

        foreach (var folderResponse in foldersResponseDict.Values)
        {
            if (folderResponse.ParentId != null)
                foldersResponseDict[folderResponse.ParentId.Value].Folders.Add(folderResponse);
        }

        foreach (var fileResponse in filesResponseDict.Values)
        {
            var parentFolderResponse = foldersResponseDict[fileResponse.FolderId];
            parentFolderResponse.Files.Add(fileResponse);
        }

        // set size for each folder
        foreach (var folderResponse in foldersResponseDict.Values)
        {
            folderResponse.Size = folderResponse.Files.Sum(x => x.Size) + folderResponse.Folders.Sum(x => x.Size);
        }

        return result;
    }
}

using FluentCMS.Api.Plugins.CmsCoreManagement.Dtos;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Controllers;

public class FoldersController(IFolderService folderService, IFileService fileService, IMapper mapper) : BaseController
{
    [HttpGet()]
    public async Task<ApiResponse<FolderDto>> GetRoot([FromQuery] Guid siteId, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.GetRoot(siteId, cancellationToken);

        var files = await fileService.GetByFolderId(folder.Id, cancellationToken);
        var folders = await folderService.GetByFolderId(folder.Id, cancellationToken);

        var folderDto = mapper.Map<FolderDto>(folder);
        folderDto.Files = mapper.Map<List<FileDto>>(files);
        folderDto.Folders = mapper.Map<List<FolderDto>>(folders);

        if (folder.ParentId != null)
        {
            var parentFolder = await folderService.GetById(folder.ParentId.Value, cancellationToken);
            folderDto.ParentFolder = mapper.Map<FolderDto>(parentFolder);
        }

        return Success(folderDto);
    }

    [HttpGet("{id}")]
    public async Task<ApiResponse<FolderDto>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.GetById(id, cancellationToken);
        var files = await fileService.GetByFolderId(folder.Id, cancellationToken);
        var folders = await folderService.GetByFolderId(folder.Id, cancellationToken);

        var folderDto = mapper.Map<FolderDto>(folder);
        folderDto.Files = mapper.Map<List<FileDto>>(files);
        folderDto.Folders = mapper.Map<List<FolderDto>>(folders);

        if (folder.ParentId != null)
        {
            var parentFolder = await folderService.GetById(folder.ParentId.Value, cancellationToken);
            folderDto.ParentFolder = mapper.Map<FolderDto>(parentFolder);
        }

        return Success(folderDto);
    }

    [HttpPost]
    public async Task<ApiResponse<FolderDto>> Create([FromBody] FolderAddRequest request, CancellationToken cancellationToken = default)
    {
        var folder = mapper.Map<Folder>(request);
        await folderService.Create(folder, cancellationToken);

        var folderDto = mapper.Map<FolderDto>(folder);
        return Success(folderDto);
    }

    [HttpPut]
    public async Task<ApiResponse<FolderDto>> Rename([FromBody] FolderRenameRequest request, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.Rename(request.Id, request.Name, cancellationToken);

        var folderDto = mapper.Map<FolderDto>(folder);

        return Success(folderDto);
    }

    [HttpPut]
    public async Task<ApiResponse<FolderDto>> Move([FromBody] FolderMoveRequest request, CancellationToken cancellationToken = default)
    {
        var folder = await folderService.Move(request.Id, request.ParentId, cancellationToken);

        var folderDto = mapper.Map<FolderDto>(folder);

        return Success(folderDto);
    }

    [HttpGet]
    public async Task<ApiListResponse<FolderDto>> GetParentFolders([FromQuery] Guid folderId, CancellationToken cancellationToken = default)
    {
        var folders = await folderService.GetParentFolders(folderId, cancellationToken);

        var response = mapper.Map<List<FolderDto>>(folders);
        return SuccessList(response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse<bool>> Remove([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await folderService.Remove(id, cancellationToken);
        return Success(true);
    }
}

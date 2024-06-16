namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileListPlugin
{
    private List<FileDetailResponse> Files { get; set; } = [];
    private List<FolderDetailResponse> Folders { get; set; } = [];

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }

    private Guid? ParentId { get; set; }

    FolderDetailResponse? FindFolderById(ICollection<FolderDetailResponse> folders, Guid folderId)
    {
        foreach (var folder in folders)
        {
            if (folder.Id == folderId)
                return folder;

            if (folder.Folders != null && folder.Folders.Any())
            {
                var foundFolder = FindFolderById(folder.Folders, folderId);
                if (foundFolder != null)
                    return foundFolder;
            }
        }
        return null;
    }

    private async Task Load()
    {
        FolderDetailResponse? folderDetail = default!;

        var folderDetailResponse = await GetApiClient<FolderClient>().GetAllAsync();

        if (FolderId is null)
        {
            folderDetail = folderDetailResponse?.Data;
        }
        else
        {
            folderDetail = FindFolderById(folderDetailResponse?.Data?.Folders, FolderId.Value);
        }

        if (folderDetail != null)
        {
            Files = folderDetail.Files.ToList() ?? [];
            Folders = folderDetail.Folders.ToList() ?? [];
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        await Load();
    }

    #region Delete File

    private FileDetailResponse? SelectedFile { get; set; }
    public async Task OnDeleteFile()
    {
        if (SelectedFile == null)
            return;

        await GetApiClient<FileClient>().DeleteAsync(SelectedFile.Id);
        await Load();
        SelectedFile = default;
    }

    public async Task OnConfirmFile(FileDetailResponse file)
    {
        SelectedFile = file;
        await Task.CompletedTask;
    }
    public async Task OnConfirmFileClose()
    {
        SelectedFile = default;
        await Task.CompletedTask;
    }
    #endregion


    #region Delete Folder

    private FolderDetailResponse? SelectedFolder { get; set; }
    public async Task OnDeleteFolder()
    {
        if (SelectedFolder == null)
            return;

        await GetApiClient<FolderClient>().DeleteAsync(SelectedFolder.Id);
        await Load();
        SelectedFolder = default;
    }

    public async Task OnConfirmFolder(FolderDetailResponse asset)
    {
        SelectedFolder = asset;
        await Task.CompletedTask;
    }
    public async Task OnConfirmFolderClose()
    {
        SelectedFolder = default;
        await Task.CompletedTask;
    }
    #endregion

}

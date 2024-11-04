namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileListPlugin
{
    private List<AssetDetail> Items { get; set; } = [];

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }
    private FolderDetailResponse? Folder { get; set; }

    private FilesTable? FilesTable { get; set; }

    private bool FolderCreateModalOpen { get; set; } = false;
    private bool FolderRenameModalOpen { get; set; } = false;
    private bool FileUploadModalOpen { get; set; } = false;
    private bool FileRenameModalOpen { get; set; } = false;

    private FolderRenameRequest? FolderRenameModel { get; set; }
    private FileRenameRequest? FileRenameModel { get; set; }
    private FolderDetailResponse? RootFolder { get; set; }
    private FileUploadConfig? FileUploadConfig { get; set; }

    private string SelectedFileExtension { get; set; } = string.Empty;

    private List<FolderDetailResponse> BreadcrumbItems { get; set; } = [];

    private string GetDownloadUrl(AssetDetail file)
    {
        return string.Join("/", BreadcrumbItems.Select(x => x.Name)) + "/" + file.Name;
    }

    #region Initialize & Lifecycle

    private async Task NavigateFolder(Guid folderId)
    {
        FolderId = folderId;
        StateHasChanged();
    }    

    protected override async Task OnInitializedAsync()
    {
        FileUploadConfig = new FileUploadConfig
        {
            AllowedExtensions = "*",
            MaxCount = 5,
            MaxSize = 1024 * 1024 * 5 // 5 mb
        };

        // TODO: Read file upload config from global settings;
        // var settingsResponse = await ApiClient.GlobalSettings.GetAsync();
        // if (settingsResponse?.Data != null)
        // {
        //     FileUploadConfig = settingsResponse.Data.FileUpload;
        // }
        await Task.CompletedTask;
    }

    #endregion

    #region Upload File

    private async Task OnUpload(List<FileParameter> files)
    {
        await ApiClient.File.UploadAsync(FolderId ?? RootFolder?.Id, files);
        FileUploadModalOpen = false;
        FilesTable?.Load();
    }

    private async Task OnUploadCancel()
    {
        FileUploadModalOpen = false;
        await Task.CompletedTask;
    }

    #endregion

    #region Create folder

    private async Task OnCreateFolder(FolderCreateRequest request)
    {
        request.SiteId = ViewState.Site.Id;
        if (request.ParentId == Guid.Empty)
            request.ParentId = RootFolder!.Id;

        await ApiClient.Folder.CreateAsync(request);
        FolderCreateModalOpen = false;
        FilesTable?.Load();
    }

    private async Task OnCreateFolderCancel()
    {
        FolderCreateModalOpen = false;
        await Task.CompletedTask;
    }
    #endregion

    #region Rename File & Folder

    private async Task OpenRenameModal(AssetDetail detail)
    {
        if (detail.IsFolder)
        {
            FolderRenameModel = new FolderRenameRequest
            {
                Id = detail.Id!,
                Name = detail.Name,
            };

            FolderRenameModalOpen = true;
        }
        else
        {
            SelectedFileExtension = Path.GetExtension(detail.Name);

            FileRenameModel = new FileRenameRequest
            {
                Id = detail.Id!,
                Name = detail.Name.Replace(SelectedFileExtension, ""),
            };

            FileRenameModalOpen = true;
        }
        await Task.CompletedTask;
    }

    private async Task OnRenameFile(FileRenameRequest request)
    {
        request.Name += SelectedFileExtension;
        FileRenameModalOpen = false;
        await ApiClient.File.RenameAsync(request);
        FilesTable?.Load();
    }

    private async Task OnRenameFileCancel()
    {
        FileRenameModalOpen = false;
        FileRenameModel = default!;

        await Task.CompletedTask;
    }

    private async Task OnRenameFolder(FolderRenameRequest request)
    {
        await ApiClient.Folder.RenameAsync(request);
        FolderRenameModalOpen = false;
        FilesTable?.Load();
    }

    private async Task OnRenameFolderCancel()
    {
        FolderRenameModalOpen = false;
        FolderRenameModel = default!;

        await Task.CompletedTask;
    }

    #endregion

    #region Move File & Folder
    private bool FolderMoveModalOpen { get; set; } = false;
    private bool FileMoveModalOpen { get; set; } = false;
    private Guid? FolderMoveModel { get; set; }
    private Guid? FileMoveModel { get; set; }

    private async Task OpenMoveModal(AssetDetail asset)
    {
        if (asset.IsFolder && !asset.IsParentFolder)
        {
            FolderMoveModel = asset.Id;
            FolderMoveModalOpen = true;
        }
        else
        {
            FileMoveModel = asset.Id;
            FileMoveModalOpen = true;
        }
        await Task.CompletedTask;
    }

    private async Task OnFolderMove(Guid parentId)
    {
        if (FolderMoveModel is null) return;

        await ApiClient.Folder.MoveAsync(new FolderMoveRequest
        {
            Id = FolderMoveModel.Value,
            ParentId = parentId,
        });
        FolderMoveModalOpen = false;
        await FilesTable?.Load();
    }

    private async Task OnFileMove(Guid parentId)
    {
        if (FileMoveModel is null) return;

        await ApiClient.File.MoveAsync(new FileMoveRequest
        {
            Id = FileMoveModel.Value,
            FolderId = parentId,
        });
        FileMoveModalOpen = false;
        FilesTable?.Load();
    }

    private async Task OnMoveFolderCancel()
    {
        FolderMoveModalOpen = false;
        FolderMoveModel = default!;
        await Task.CompletedTask;
    }

    private async Task OnMoveFileCancel()
    {
        FileMoveModalOpen = false;
        FileMoveModel = default!;
        await Task.CompletedTask;
    }

    #endregion

    #region Delete File & Folder

    private AssetDetail? SelectedItem { get; set; }
    public async Task OnDelete()
    {
        if (SelectedItem == null)
            return;

        if (SelectedItem.IsFolder)
        {
            await ApiClient.Folder.DeleteAsync(SelectedItem.Id);
        }
        else
        {
            await ApiClient.File.DeleteAsync(SelectedItem.Id);
        }
        FilesTable?.Load();
        SelectedItem = default;
    }

    public async Task OnConfirm(AssetDetail item)
    {
        SelectedItem = item;
        await Task.CompletedTask;
    }
    public async Task OnConfirmClose()
    {
        SelectedItem = default;
        await Task.CompletedTask;
    }
    #endregion
}

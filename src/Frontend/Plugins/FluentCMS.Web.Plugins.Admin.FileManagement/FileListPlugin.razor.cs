namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileListPlugin
{
    private List<AssetDetail> Items { get; set; } = [];

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }

    private Guid? ParentId { get; set; }

    private bool FolderCreateModalOpen { get; set; } = false;
    private bool FolderUpdateModalOpen { get; set; } = false;
    private bool FileUploadModalOpen { get; set; } = false;
    private bool FileUpdateModalOpen { get; set; } = false;

    private FolderUpdateRequest FolderUpdateModel { get; set; }
    private FileUpdateRequest FileUpdateModel { get; set; }
    private FolderDetailResponse RootFolder { get; set; }
    private FileUploadConfiguration FileUploadConfig { get; set; }

    private string SelectedFileExtension { get; set; } = string.Empty;

    #region Helper Methods

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

    #endregion

    #region Initialize & Lifecycle

    protected override async Task OnInitializedAsync()
    {
        var settingsResponse = await GetApiClient<GlobalSettingsClient>().GetAsync();
        if (settingsResponse?.Data != null)
        {
            FileUploadConfig = settingsResponse?.Data.FileUpload;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        await Load();
    }

    private async Task Load()
    {
        var rootFolderResponse = await GetApiClient<FolderClient>().GetAllAsync();
        RootFolder = rootFolderResponse?.Data;

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
            ParentId = folderDetail.FolderId;

            Items = [];

            if (FolderId != null && FolderId != Guid.Empty)
            {
                Items.Add(new AssetDetail
                {
                    Name = "(parent)",
                    IsFolder = true,
                    Id = folderDetail.FolderId == Guid.Empty ? null : folderDetail.FolderId,
                    IsParentFolder = true
                });
            }

            foreach (var item in folderDetail.Folders)
            {
                Items.Add(new AssetDetail
                {
                    Name = item.Name,
                    IsFolder = true,
                    Id = item.Id,
                    FolderId = item.FolderId,
                    Size = item.Size,
                });
            }

            foreach (var item in folderDetail.Files)
            {
                Items.Add(new AssetDetail
                {
                    Name = item.Name,
                    IsFolder = false,
                    FolderId = item.FolderId,
                    Id = item.Id,
                    Size = item.Size,
                    ContentType = item.ContentType
                });
            }
        }
    }

    #endregion

    #region Upload File

    private async Task OnUpload(List<FileParameter> files)
    {
        await GetApiClient<FileClient>().UploadAsync(FolderId, files);
        FileUploadModalOpen = false;
        await Load();
    }

    private async Task OnUploadCancel()
    {
        FileUploadModalOpen = false;
    }

    #endregion

    #region Create folder

    private async Task OnCreateFolder(FolderCreateRequest request)
    {
        await GetApiClient<FolderClient>().CreateAsync(request);
        FolderCreateModalOpen = false;
        await Load();
    }

    private async Task OnCreateFolderCancel()
    {
        FolderCreateModalOpen = false;
    }
    #endregion

    #region Update File & Folder

    private async Task OpenUpdateModal(AssetDetail detail)
    {
        if (detail.IsFolder)
        {
            FolderUpdateModel = new FolderUpdateRequest
            {
                Id = detail.Id.Value,
                Name = detail.Name,
                FolderId = detail.FolderId ?? Guid.Empty
            };

            FolderUpdateModalOpen = true;
        }
        else
        {
            SelectedFileExtension = System.IO.Path.GetExtension(detail.Name);

            FileUpdateModel = new FileUpdateRequest
            {
                Id = detail.Id.Value,
                Name = detail.Name.Replace(SelectedFileExtension, ""),
                FolderId = detail.FolderId ?? Guid.Empty
            };

            FileUpdateModalOpen = true;
        }
    }

    private async Task OnUpdateFile(FileUpdateRequest request)
    {
        request.Name = request.Name + SelectedFileExtension;
        FileUpdateModalOpen = false;
        await GetApiClient<FileClient>().UpdateAsync(request);
        await Load();
    }

    private async Task OnUpdateFileCancel()
    {
        FileUpdateModalOpen = false;
        FileUpdateModel = default!;
    }

    private async Task OnUpdateFolder(FolderUpdateRequest request)
    {
        await GetApiClient<FolderClient>().UpdateAsync(request);
        FolderUpdateModalOpen = false;
        await Load();
    }

    private async Task OnUpdateFolderCancel()
    {
        FolderUpdateModalOpen = false;
        FolderUpdateModel = default!;
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
            await GetApiClient<FolderClient>().DeleteAsync(SelectedItem.Id.Value);
        }
        else
        {
            await GetApiClient<FileClient>().DeleteAsync(SelectedItem.Id.Value);
        }
        await Load();
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

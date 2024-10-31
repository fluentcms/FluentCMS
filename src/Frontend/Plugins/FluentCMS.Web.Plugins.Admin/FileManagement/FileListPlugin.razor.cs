namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileListPlugin
{
    private List<AssetDetail> Items { get; set; } = [];
    private List<FolderBreadcrumbItemType> BreadcrumbItems { get; set; } = [];

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }
    private Guid? ParentFolderId { get; set; }

    private bool FolderCreateModalOpen { get; set; } = false;
    private bool FolderRenameModalOpen { get; set; } = false;
    private bool FileUploadModalOpen { get; set; } = false;
    private bool FileRenameModalOpen { get; set; } = false;

    private FolderRenameRequest? FolderRenameModel { get; set; }
    private FileRenameRequest? FileRenameModel { get; set; }
    private FolderDetailResponse? RootFolder { get; set; }
    private FileUploadConfig? FileUploadConfig { get; set; }

    private string SelectedFileExtension { get; set; } = string.Empty;

    #region Helper Methods

    FolderDetailResponse? FindFolderById(ICollection<FolderDetailResponse>? folders, Guid folderId)
    {
        if (folders is null)
            return null;

        foreach (var folder in folders)
        {
            if (folder.Id == folderId)
            {
                BreadcrumbItems.Add(new FolderBreadcrumbItemType
                {
                    Title = folder.Name,
                });
                return folder;
            }

            if (folder.Folders != null && folder.Folders.Count > 0)
            {
                var foundFolder = FindFolderById(folder.Folders, folderId);
                if (foundFolder != null)
                {
                    BreadcrumbItems.Add(new FolderBreadcrumbItemType
                    {
                        Title = folder.Name,
                        Href = GetUrl("Files List", new { folderId = folder.Id })
                    });
                    return foundFolder;
                }
            }
        }
        return null;
    }

    #endregion

    #region Initialize & Lifecycle

    protected override async Task OnInitializedAsync()
    {
        var settingsResponse = await ApiClient.GlobalSettings.GetAsync();
        if (settingsResponse?.Data != null)
        {
            FileUploadConfig = settingsResponse.Data.FileUpload;
        }
        await Task.CompletedTask;
    }

    protected override async Task OnParametersSetAsync()
    {
        await Load();
    }

    private async Task Load()
    {
        BreadcrumbItems = [];
        FolderDetailResponse? folderDetail = default!;

        var folderDetailResponse = await ApiClient.Folder.GetAllAsync(ViewState.Site.Id);

        if (folderDetailResponse?.Data != null)
        {
           RootFolder = folderDetailResponse.Data;

            if (FolderId is null || FolderId == Guid.Empty || FolderId == RootFolder.Id)
            {
                BreadcrumbItems.Add(new FolderBreadcrumbItemType
                {
                    Icon = IconName.Folder,
                    Title = "Root"
                });
                folderDetail = RootFolder;
            }
            else
            {
                folderDetail = FindFolderById(RootFolder!.Folders, FolderId.Value);

               BreadcrumbItems.Add(new FolderBreadcrumbItemType
               {
                   Icon = IconName.Folder,
                   Title = "Root",
                   Href = GetUrl("Files List", new { folderId = Guid.Empty })
               });
               BreadcrumbItems.Reverse();
           }

            if (folderDetail != null)
            {
                Items = [];

                if (FolderId != null && FolderId != Guid.Empty && FolderId != RootFolder.Id)
                {
                    Items.Add(new AssetDetail
                    {
                        Name = "(parent)",
                        IsFolder = true,
                        Id = folderDetail.ParentId.Value == RootFolder.Id ? Guid.Empty : folderDetail.ParentId.Value,
                        IsParentFolder = true
                    });
                }

                if (folderDetail != null)
                {
                    foreach (var item in folderDetail.Folders)
                    {
                        Items.Add(new AssetDetail
                        {
                            Name = item.Name,
                            IsFolder = true,
                            Id = item.Id,
                            ParentId = item.ParentId,
                            Size = item.Size,
                        });
                    }

                    foreach (var item in folderDetail.Files)
                    {
                        Items.Add(new AssetDetail
                        {
                            Name = item.Name,
                            IsFolder = false,
                            ParentId = item.FolderId,
                            Id = item.Id,
                            Size = item.Size,
                            ContentType = item.ContentType
                        });
                    }
                }
            }
        }
        ParentFolderId = folderDetail?.ParentId;
    }

    #endregion

    #region Upload File

    private async Task OnUpload(List<FileParameter> files)
    {
        await ApiClient.File.UploadAsync(FolderId, files);
        FileUploadModalOpen = false;
        await Load();
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
        if(request.ParentId == Guid.Empty)
            request.ParentId = RootFolder!.Id;

        await ApiClient.Folder.CreateAsync(request);
        FolderCreateModalOpen = false;
        await Load();
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
       request.Name = request.Name + SelectedFileExtension;
       FileRenameModalOpen = false;
       await ApiClient.File.RenameAsync(request);
       await Load();
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
        await Load();
    }

    private async Task OnRenameFolderCancel()
    {
        FolderRenameModalOpen = false;
        FolderRenameModel = default!;

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

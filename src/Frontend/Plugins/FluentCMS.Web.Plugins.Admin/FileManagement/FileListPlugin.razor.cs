using FluentCMS.Web.UI.Components;

namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileListPlugin
{
    private List<AssetDetail> Items { get; set; } = [];
    private List<FolderBreadcrumbItemType> BreadcrumbItems { get; set; } = [];

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }

    private Guid? ParentId { get; set; }

    private bool FolderCreateModalOpen { get; set; } = false;
    private bool FolderUpdateModalOpen { get; set; } = false;
    private bool FileUploadModalOpen { get; set; } = false;
    private bool FileUpdateModalOpen { get; set; } = false;

    private FolderUpdateRequest? FolderUpdateModel { get; set; }
    private FileUpdateRequest? FileUpdateModel { get; set; }
    private FolderDetailResponse? RootFolder { get; set; }
    private FileUploadConfiguration? FileUploadConfig { get; set; }

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
                BreadcrumbItems.Add( new FolderBreadcrumbItemType {
                    Title = folder.Name,
                });
                return folder;
            }

            if (folder.Folders != null && folder.Folders.Any())
            {
                var foundFolder = FindFolderById(folder.Folders, folderId);
                if (foundFolder != null)
                {
                    BreadcrumbItems.Add( new FolderBreadcrumbItemType {
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

        var folderDetailResponse = await ApiClient.Folder.GetAllAsync();

        if (folderDetailResponse?.Data != null)
        {
            RootFolder = folderDetailResponse.Data;

            if (FolderId is null || FolderId == Guid.Empty)
            {
                BreadcrumbItems.Add( new FolderBreadcrumbItemType {
                    Icon = IconName.Folder,
                    Title = "Root"
                });
                folderDetail = RootFolder;
            }
            else
            {
                folderDetail = FindFolderById(RootFolder!.Folders, FolderId.Value);

                BreadcrumbItems.Add( new FolderBreadcrumbItemType {
                    Icon = IconName.Folder,
                    Title = "Root",
                    Href = GetUrl("Files List", new { folderId = Guid.Empty }) 
                });
                BreadcrumbItems.Reverse();
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
                        Id = folderDetail.FolderId,
                        IsParentFolder = true
                    });
                }

                if(folderDetail != null)
                {
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
        }
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

    #region Update File & Folder

    private async Task OpenUpdateModal(AssetDetail detail)
    {
        if (detail.IsFolder)
        {
            FolderUpdateModel = new FolderUpdateRequest
            {
                Id = detail.Id!,
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
                Id = detail.Id!,
                Name = detail.Name.Replace(SelectedFileExtension, ""),
                FolderId = detail.FolderId ?? Guid.Empty
            };

            FileUpdateModalOpen = true;
        }
        await Task.CompletedTask;
    }

    private async Task OnUpdateFile(FileUpdateRequest request)
    {
        request.Name = request.Name + SelectedFileExtension;
        FileUpdateModalOpen = false;
        await ApiClient.File.UpdateAsync(request);
        await Load();
    }

    private async Task OnUpdateFileCancel()
    {
        FileUpdateModalOpen = false;
        FileUpdateModel = default!;

        await Task.CompletedTask;
    }

    private async Task OnUpdateFolder(FolderUpdateRequest request)
    {
        await ApiClient.Folder.UpdateAsync(request);
        FolderUpdateModalOpen = false;
        await Load();
    }

    private async Task OnUpdateFolderCancel()
    {
        FolderUpdateModalOpen = false;
        FolderUpdateModel = default!;

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

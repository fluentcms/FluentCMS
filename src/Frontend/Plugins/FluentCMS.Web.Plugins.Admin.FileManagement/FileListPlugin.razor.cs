namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileListPlugin
{
    private List<AssetDetailResponse> Assets { get; set; } = [];

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }

    private Guid? ParentId { get; set; }

    private async Task Load()
    {
        if (FolderId != null)
        {
            var folderDetailResponse = await GetApiClient<FolderClient>().GetByIdAsync(FolderId.Value);
            ParentId = folderDetailResponse?.Data?.FolderId;
        }

        var response = await GetApiClient<FolderClient>().GetAllAsync(FolderId);
        Assets = response?.Data?.ToList() ?? [];
    }

    protected override async Task OnParametersSetAsync()
    {
        await Load();
    }

    #region Delete File

    private AssetDetailResponse? SelectedAsset { get; set; }
    public async Task OnDelete()
    {
        if (SelectedAsset == null)
            return;

        if (SelectedAsset.Type == AssetType.Folder)
        {
            await GetApiClient<FolderClient>().DeleteAsync(SelectedAsset.Id);
        }
        else
        {
            await GetApiClient<FileClient>().DeleteAsync(SelectedAsset.Id);
        }
        await Load();
        SelectedAsset = default;
    }

    public async Task OnConfirm(AssetDetailResponse asset)
    {
        SelectedAsset = asset;
        await Task.CompletedTask;
    }
    public async Task OnConfirmClose()
    {
        SelectedAsset = default;
        await Task.CompletedTask;
    }
    #endregion

}

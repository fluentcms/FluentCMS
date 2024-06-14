namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileListPlugin
{
    private List<AssetDetailResponse> Assets { get; set; } = [];

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }

    private async Task Load()
    {
        Console.WriteLine($"ID: {FolderId}");
        var response = await GetApiClient<FolderClient>().GetAllAsync(FolderId);
        Assets = response?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }

    #region Delete File

    private AssetDetailResponse? SelectedAsset { get; set; }
    public async Task OnDelete()
    {
        if (SelectedAsset == null)
            return;

        await GetApiClient<FileClient>().DeleteAsync(SelectedAsset.Id);
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

namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderSelectorModal
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;
 
    [Inject]
    private ViewState ViewState { get; set; } = default!;
    
    [Parameter]
    public EventCallback<Guid> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public Guid? Model { get; set; } = default!;

    private bool FolderModalOpen { get; set; } = false;

    private List<AssetDetail> Assets { get; set; } = [];

    private FolderDetailResponse RootFolder { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var rootFolderDetailResponse = await ApiClient.Folder.GetAllAsync(ViewState.Site.Id);

        RootFolder = rootFolderDetailResponse.Data;
        Model ??= RootFolder.Id;

        await Load();
    }
    private bool TableAction { get; set; } = true;

    private async Task DownloadFile(Guid fileId)
    {
        await Task.CompletedTask;
    }

    private async Task Load()
    {
        if(Model is null) return;
        Assets = [];

        var folderDetailResponse = await ApiClient.Folder.GetByIdAsync(Model.Value);
        var folderDetail = folderDetailResponse.Data;

        if (folderDetail != null)
        {

            if(RootFolder.Id != Model && folderDetail.ParentId != null)
            {
                Assets.Add(new AssetDetail
                {
                    Name = "(parent)",
                    IsFolder = true,
                    Id = folderDetail.ParentId.Value,
                    IsParentFolder = true
                });
            }

            foreach (var item in folderDetail.Folders ?? [])
            {
                Assets.Add(new AssetDetail
                {
                    Name = item.Name ?? string.Empty,
                    IsFolder = true,
                    Id = item.Id,
                    ParentId = item.ParentId,
                    Size = item.Size,
                });
            }

            foreach (var item in folderDetail.Files ?? [])
            {
                Assets.Add(new AssetDetail
                {
                    Name = item.Name ?? string.Empty,
                    IsFolder = false,
                    ParentId = item.FolderId,
                    Id = item.Id,
                    Size = item.Size,
                    ContentType = item.ContentType
                });
            }
        }

        await Task.CompletedTask;
    }

    private async Task OnNavigateFolder(Guid folderId)
    {
        Model = folderId;

        await Load();
    }

    private async Task OnChooseFolder()
    {
        if(Model is null) return;
        await OnSubmit.InvokeAsync(Model.Value);
    }

    private async Task OnFolderModalClose() 
    {
        FolderModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}

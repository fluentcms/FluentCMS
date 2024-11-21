namespace FluentCMS.Web.Plugins;

public partial class FilesTable
{
    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    [Inject]
    protected IHttpContextAccessor? HttpContextAccessor { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public RenderFragment<AssetDetail> ActionsFragment { get; set; } = default!;

    [Parameter]
    public bool HideFiles { get; set; } = false;

    [Parameter]
    public EventCallback<AssetDetail> OnRowClick { get; set; } = default!;

    [Parameter]
    public Guid? DisabledFolder { get; set; }

    [Parameter]
    public List<FolderDetailResponse> ParentFolders { get; set; } = [];

    [Parameter]
    public EventCallback<List<FolderDetailResponse>> ParentFoldersChanged { get; set; }

    [Parameter]
    public FolderDetailResponse? Folder { get; set; } = default!;

    [Parameter]
    public EventCallback<FolderDetailResponse?> FolderChanged { get; set; }

    [Parameter]
    public Guid? FolderId { get; set; } = default!;

    [Parameter]
    public EventCallback<Guid?> FolderIdChanged { get; set; }

    [Parameter]
    public FolderDetailResponse? RootFolder { get; set; } = default!;

    [Parameter]
    public EventCallback<FolderDetailResponse> RootFolderChanged { get; set; }

    private List<AssetDetail> Items { get; set; } = [];

    private async Task HandleRowClick(AssetDetail item)
    {
        if (item.Id == DisabledFolder)
            return;

        if (item.IsFolder)
            await NavigateFolder(item.Id);

        await OnRowClick.InvokeAsync(item);
    }

    private IconName GetFileIcon(AssetDetail item)
    {
        if (item.IsParentFolder)
            return IconName.ParentFolder;

        if (item.IsFolder)
            return IconName.Folder;

        if (item.ContentType?.StartsWith("image/") ?? false)
            return IconName.Image;

        return IconName.File;
    }

    private static string HumanizeFileSize(long? size)
    {
        if (size is null || size == 0) return "...";

        if (size > 1024 * 1024)
            return $"{size / (1024 * 1024)} MB";
        else if (size > 1024)
            return $"{size / 1024} KB";

        return $"{size} Bytes";
    }

    public async Task Load()
    {
        Items = [];

        var folderDetailResponse = await ApiClient.Folder.GetByIdAsync(FolderId.Value);
        var folderDetail = folderDetailResponse.Data;

        if (folderDetail != null)
        {
            Folder = folderDetail;
            await FolderChanged.InvokeAsync(Folder);

            if (RootFolder?.Id != FolderId && folderDetail.ParentId != null)
            {
                Items.Add(new AssetDetail
                {
                    Name = "(parent)",
                    IsFolder = true,
                    Id = folderDetail.ParentId.Value,
                    IsParentFolder = true
                });
            }

            foreach (var item in folderDetail.Folders ?? [])
            {
                Items.Add(new AssetDetail
                {
                    Name = item.Name ?? string.Empty,
                    IsFolder = true,
                    Id = item.Id,
                    ParentId = item.ParentId,
                    Size = item.Size,
                });
            }

            if (!HideFiles)
            {
                foreach (var item in folderDetail.Files ?? [])
                {
                    Items.Add(new AssetDetail
                    {
                        Name = item.Name ?? string.Empty,
                        IsFolder = false,
                        ParentId = item.FolderId,
                        Path = item.Path,
                        Id = item.Id,
                        Size = item.Size,
                        ContentType = item.ContentType
                    });
                }
            }
        }
        await Task.CompletedTask;

        var parentFoldersResponse = await ApiClient.Folder.GetParentFoldersAsync(FolderId.Value);
        ParentFolders = parentFoldersResponse.Data?.ToList() ?? [];
        await ParentFoldersChanged.InvokeAsync(ParentFolders);

        StateHasChanged();
    }

    protected override async Task OnParametersSetAsync()
    {
        FolderId ??= RootFolder?.Id;

        if (FolderId is null)
            return;
    }

    private async Task NavigateFolder(Guid? folderId)
    {
        if (folderId is null)
            FolderId = RootFolder?.Id;
        else
            FolderId = folderId;

        await FolderIdChanged.InvokeAsync(FolderId);
        await Load();
    }

    private async Task DownloadFile(Guid id)
    {
        await Task.CompletedTask;
        //
    }

    protected override async Task OnInitializedAsync()
    {
        var rootFolderDetailResponse = await ApiClient.Folder.GetAllAsync(ViewState.Site.Id);
        RootFolder = rootFolderDetailResponse.Data;
        await RootFolderChanged.InvokeAsync(RootFolder);

        if (FolderId is null)
        {
            FolderId ??= RootFolder?.Id;
            await FolderIdChanged.InvokeAsync(FolderId.Value);
        }

        await Load();
    }
}

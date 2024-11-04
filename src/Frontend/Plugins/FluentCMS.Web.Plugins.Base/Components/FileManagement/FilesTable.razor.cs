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

    private static string ParentFolderIcon => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"M11 17h2v-4.2l1.6 1.6L16 13l-4-4l-4 4l1.4 1.4l1.6-1.6zm-7 3q-.825 0-1.412-.587T2 18V6q0-.825.588-1.412T4 4h6l2 2h8q.825 0 1.413.588T22 8v10q0 .825-.587 1.413T20 20zm0-2h16V8h-8.825l-2-2H4zm0 0V6z\"/></svg>";
    private static string FolderIcon => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"M4 20q-.825 0-1.412-.587T2 18V6q0-.825.588-1.412T4 4h6l2 2h8q.825 0 1.413.588T22 8v10q0 .825-.587 1.413T20 20zm0-2h16V8h-8.825l-2-2H4zm0 0V6z\"/></svg>";
    private static string FileIcon => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"M6 2a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zm0 2h7v5h5v11H6zm2 8v2h8v-2zm0 4v2h5v-2z\"/></svg>";
    private static string ImageIcon => "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 24 24\"><path fill=\"currentColor\" d=\"m14 2l6 6v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2zm4 18V9h-5V4H6v16zm-1-7v6H7l5-5l2 2m-4-5.5A1.5 1.5 0 0 1 8.5 12A1.5 1.5 0 0 1 7 10.5A1.5 1.5 0 0 1 8.5 9a1.5 1.5 0 0 1 1.5 1.5\"/></svg>";

    private async Task HandleRowClick(AssetDetail item)
    {
        if(item.Id == DisabledFolder)
            return;

        if(item.IsFolder)
            await NavigateFolder(item.Id);

        await OnRowClick.InvokeAsync(item);
    }

    private string GetFileIcon(AssetDetail item)
    {
        if (item.IsParentFolder)
            return ParentFolderIcon;

        if (item.IsFolder)
            return FolderIcon;

        if (item.ContentType?.StartsWith("image/") ?? false)
            return ImageIcon;

        return FileIcon;
    }

    private static string HumanizeFileSize(long? size)
    {
        if(size is null || size == 0) return "...";

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

            foreach (var item in folderDetail.Files ?? [])
            {
                Items.Add(new AssetDetail
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

        var parentFoldersResponse = await ApiClient.Folder.GetParentFoldersAsync(FolderId.Value);
        ParentFolders = parentFoldersResponse.Data?.ToList() ?? [];
        await ParentFoldersChanged.InvokeAsync(ParentFolders);

        StateHasChanged();
    }

    protected override async Task OnParametersSetAsync()
    {
        FolderId ??= RootFolder?.Id;

        if(FolderId is null)
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

        if(FolderId is null)
        {
            FolderId ??= RootFolder?.Id;
            await FolderIdChanged.InvokeAsync(FolderId.Value);
        }

        await Load();
    }
}

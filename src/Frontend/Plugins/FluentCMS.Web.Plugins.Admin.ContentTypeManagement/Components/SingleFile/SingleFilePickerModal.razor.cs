namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SingleFilePickerModal
{
    [Parameter]
    public Guid Model { get; set; }

    [Parameter]
    public bool Visible { get; set; } = true;

    [Parameter]
    public EventCallback<Guid> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private Guid? CurrentFolderId { get; set; }
    private Guid? ParentId { get; set; }

    private bool FileUploadModalOpen { get; set; }
    private FolderDetailResponse RootFolder { get; set; }
    private List<AssetDetail> Items { get; set; } = [];

    private Guid SelectedFile { get; set; }

    public async Task OnUploadClicked()
    {
        FileUploadModalOpen = true;
    }


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
        var rootFolderResponse = await GetApiClient<FolderClient>().GetAllAsync();
        RootFolder = rootFolderResponse?.Data;

        FolderDetailResponse? folderDetail = default!;

        var folderDetailResponse = await GetApiClient<FolderClient>().GetAllAsync();

        if (CurrentFolderId is null || CurrentFolderId == Guid.Empty)
        {
            folderDetail = folderDetailResponse?.Data;
        }
        else
        {
            folderDetail = FindFolderById(folderDetailResponse?.Data?.Folders, CurrentFolderId.Value);
        }

        if (folderDetail != null)
        {
            ParentId = folderDetail.FolderId;

            Items = [];

            if (CurrentFolderId != null && CurrentFolderId != Guid.Empty)
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

    protected override async Task OnParametersSetAsync()
    {
        await Load();
    }

    private async Task OnUpload(List<FileParameter> files)
    {
        await GetApiClient<FileClient>().UploadAsync(CurrentFolderId, files);
        FileUploadModalOpen = false;
        await Load();
    }
    
    public async Task OnUpload()
    {
        FileUploadModalOpen = true;
        Console.WriteLine("Open local File Picker");
    }

    public async Task OnUploadCancel()
    {
        FileUploadModalOpen = false;
    }

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync(SelectedFile);
        SelectedFile = default!;
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}
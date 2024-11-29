namespace FluentCMS.Web.Plugins;

public partial class FileSelectorModal
{
    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Parameter]
    public EventCallback<AssetDetail> OnSubmit { get; set; }

    [CascadingParameter]
    public FilesTable FilesTable { get; set; } = default!;

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public Guid? FolderId { get; set; }

    [Parameter]
    public Guid? Model { get; set; } = default!;

    private List<FolderDetailResponse> ParentFolders { get; set; } = [];
    private bool FileUploadModalOpen { get; set; } = false;

    private FileUploadConfig? FileUploadConfig { get; set; }

    private FolderDetailResponse? RootFolder { get; set; }

    private async Task OpenFileUpload()
    {
        // FileUplaodRef;
        //        FileUploadModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task NavigateFolder(Guid id)
    {
        FolderId = id;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private string GetDownloadUrl(AssetDetail file)
    {
        return string.Join("/", ParentFolders.Select(x => x.Name)) + "/" + file.Name;
    }

    private async Task OnSelectFile(AssetDetail item)
    {
        if (item.IsFolder) return;

        Model = item.Id;
        await OnSubmit.InvokeAsync(item);
    }

    private async Task HandleFilesChanged(InputFileChangeEventArgs e)
    {
        List<FileParameter> Files = [];
        foreach (var file in e.GetMultipleFiles(FileUploadConfig!.MaxCount))
        {
            var Data = file.OpenReadStream(FileUploadConfig!.MaxSize);
            Files.Add(new FileParameter(Data, file.Name, file.ContentType));
        }

        var result = await ApiClient.File.UploadAsync(FolderId, Files);
        await FilesTable?.Load();
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

    protected override async Task OnInitializedAsync()
    {
        var settingsResponse = await ApiClient.GlobalSettings.GetAsync();
        if (settingsResponse?.Data != null)
        {
            FileUploadConfig = settingsResponse.Data.FileUpload ?? new FileUploadConfig
            {
                AllowedExtensions = "*",
                MaxCount = 5,
                MaxSize = 1024 * 1024 * 5 // 5 mb
            };
        }
    }
}

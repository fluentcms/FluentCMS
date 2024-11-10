namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiFileFieldFormFilesPicker
{
    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected ViewState ViewState { get; set; } = default!;

    private List<FileParameter> Files { get; set; } = [];

    private FolderDetailResponse RootFolder { get; set; }
    private Dictionary<Guid, string> FileUrlsDict { get; set; } = [];

    private async Task OnFilesChanged(InputFileChangeEventArgs e)
    {
        Files = [];
        foreach (var file in e.GetMultipleFiles(FileUploadConfig!.MaxCount))
        {
            var Data = file.OpenReadStream(FileUploadConfig!.MaxSize);
            Files.Add(new FileParameter(Data, file.Name, file.ContentType));
        }

        var result = await ApiClient.File.UploadAsync(RootFolder.Id, Files);
        if (result?.Data != null && result.Data.Count > 0)
        {
            foreach (var item in result.Data)
            {
                var url = await GetFileUrl(item);
                FileUrlsDict.Add(item.Id, url);
                FieldValue.Value.Add(item.Id);
            }
        }
    }

    private async Task RemoveValue(Guid item)
    {
        FieldValue.Value.Remove(item);
        await Task.CompletedTask;
    }

    private string GetFileName(Guid item)
    { 
        return FileUrlsDict[item].Split("/").Last();
    }

    private string GetFileUrl(Guid item)
    {
        return FileUrlsDict[item];
    }

    private async Task<string> GetFileUrl(FileDetailResponse file)
    {
        var foldersResponse = await ApiClient.Folder.GetParentFoldersAsync(file.FolderId);
        return string.Join("/", (foldersResponse.Data ?? []).Select(x => x.Name)) + "/" + file.Name; 
    }

    private FileUploadConfig? FileUploadConfig { get; set; }

    protected override async Task OnInitializedAsync()
    {
        // var settingsResponse = await ApiClient.GlobalSettings.GetAsync();
        // if (settingsResponse?.Data != null)
        // {
            // FileUploadConfig = settingsResponse.Data.FileUpload ?? new FileUploadConfig
            FileUploadConfig = new FileUploadConfig
            {
                AllowedExtensions = "*",
                MaxCount = 5,
                MaxSize = 1024 * 1024 * 5 // 5 mb
            };
        // }

        if (FieldValue.Value is null)
            FieldValue.Value = [];

        if (FieldValue.Value.Count > 0)
        {
            foreach (var item in FieldValue.Value)
            {
                var fileResponse = await ApiClient.File.GetByIdAsync(item);

                var url = await GetFileUrl(fileResponse.Data);
                FileUrlsDict.Add(item, url);
            }
        }

        var rootFolderResponse = await ApiClient.Folder.GetAllAsync(ViewState.Site.Id);
        RootFolder = rootFolderResponse.Data;
    }
}

namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiFileFieldFormFilesPicker
{
    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    protected ViewState ViewState { get; set; } = default!;

    private FolderDetailResponse? RootFolder { get; set; }
    private Dictionary<Guid, string> FileUrlsDict { get; set; } = [];

    private bool FileSelectorModalOpen { get; set; } = false;

    private async Task OpenFileSelectorModal()
    {
        FileSelectorModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task CloseFileSelectorModal()
    {
        FileSelectorModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task OnFileSelectorSubmit(AssetDetail file)
    {
        FileSelectorModalOpen = false;
        FieldValue.Value.Add(file.Id);
        await Load();
    }

    private async Task RemoveValue(Guid item)
    {
        FieldValue.Value.Remove(item);
        await Task.CompletedTask;
    }

    private string GetFileName(Guid item)
    {
        FileUrlsDict.TryGetValue(item, out var fileUrl);
        if (!string.IsNullOrEmpty(fileUrl))
            return fileUrl.Split("/").Last();

        return "";
    }

    private string GetFileUrl(Guid item)
    {
        FileUrlsDict.TryGetValue(item, out var fileUrl);
        if (!string.IsNullOrEmpty(fileUrl))
            return fileUrl;

        return "";
    }

    private async Task Load()
    {
        FileUrlsDict = [];
        if (FieldValue.Value.Count > 0)
        {
            foreach (var item in FieldValue.Value)
            {
                var fileResponse = await ApiClient.File.GetByIdAsync(item);

                FileUrlsDict.Add(item, fileResponse.Data.Path!);
            }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (FieldValue.Value is null)
            FieldValue.Value = [];
    
        var rootFolderResponse = await ApiClient.Folder.GetAllAsync(ViewState.Site.Id);
        RootFolder = rootFolderResponse.Data;

        await Load();
    }
}

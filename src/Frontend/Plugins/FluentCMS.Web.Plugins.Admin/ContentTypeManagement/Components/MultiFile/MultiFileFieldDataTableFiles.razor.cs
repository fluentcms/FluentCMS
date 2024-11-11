namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiFileFieldDataTableFiles
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;
    private Dictionary<Guid, string> FileUrlsDict { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        foreach (var item in FieldValue.Value)
        {
            var fileResponse = await ApiClient.File.GetByIdAsync(item);
            var foldersResponse = await ApiClient.Folder.GetParentFoldersAsync(fileResponse.Data.FolderId);
            var fileName = string.Join("/", (foldersResponse.Data ?? []).Select(x => x.Name)) + "/" + fileResponse.Data.Name;

            FileUrlsDict.Add(fileResponse.Data.Id, fileName);
        }
    }

    private string GetFileName(Guid file)
    {
        return FileUrlsDict[file].Split("/").Last();
    }

    private string GetDownloadUrl(Guid file)
    {
        return FileUrlsDict[file];
    }
}

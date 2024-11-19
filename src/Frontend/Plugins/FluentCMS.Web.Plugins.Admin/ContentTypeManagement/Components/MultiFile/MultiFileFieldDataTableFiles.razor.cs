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
            try
            {
                var fileResponse = await ApiClient.File.GetByIdAsync(item);
                FileUrlsDict.Add(fileResponse.Data.Id, fileResponse.Data.Path);
            }
            catch (Exception)
            {
                // File not found
            }
        }
    }

    private string GetFileName(Guid file)
    {
        FileUrlsDict.TryGetValue(file, out string fileName);

        if (string.IsNullOrEmpty(fileName))
            return "";

        return fileName.Split("/").Last();
    }

    private string GetDownloadUrl(Guid file)
    {
        FileUrlsDict.TryGetValue(file, out string fileName);

        if (string.IsNullOrEmpty(fileName))
            return "";

        return fileName;
    }
}

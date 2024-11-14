namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SingleFileFieldDataTableFile
{
    [Inject]
    private ViewState ViewState { get; set; }

    [Inject]
    private ApiClientFactory ApiClient { get; set; }

    private string FileName { get; set; } = string.Empty;
    private string FilePath { get; set; } = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (FieldValue.Value != null)
        {
            var fileResponse = await ApiClient.File.GetByIdAsync(FieldValue.Value.Value);
            var parentFoldersResponse = await ApiClient.Folder.GetParentFoldersAsync(fileResponse.Data.FolderId);

            FileName = fileResponse.Data.Name;
            FilePath = string.Join("/", parentFoldersResponse.Data.Select(x => x.Name)) + "/" + fileResponse.Data.Name;
        }
    }
}
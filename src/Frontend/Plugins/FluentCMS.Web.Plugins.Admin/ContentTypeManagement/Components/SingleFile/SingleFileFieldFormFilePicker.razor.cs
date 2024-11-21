namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SingleFileFieldFormFilePicker
{
    [Inject]
    protected ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    private List<FileParameter> Files { get; set; } = [];

    private FolderDetailResponse RootFolder { get; set; } = default!;

    private bool FileSelectorModalOpen { get; set; } = false;

    private string FileUrl { get; set; } = string.Empty;
    private string FileName { get; set; } = string.Empty;
    private bool TableAction { get; set; } = true;

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
        FieldValue.Value = file.Id;
        await Load();
    }

    protected override async Task OnInitializedAsync()
    {
        var rootFolderResponse = await ApiClient.Folder.GetAllAsync(ViewState.Site.Id);
        RootFolder = rootFolderResponse.Data;
        await Load();
    }

    private async Task Load()
    {
        if (FieldValue.Value != null)
        {
            var fileResponse = await ApiClient.File.GetByIdAsync(FieldValue.Value.Value);

            if (fileResponse.Data != null)
            {
                FileUrl = fileResponse?.Data.Path ?? string.Empty;
                FileName = fileResponse?.Data.Name ?? string.Empty;
            }
        }
    }

}

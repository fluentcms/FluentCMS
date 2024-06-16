namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileDetailPlugin
{
    private FileDetailResponse Model { get; set; }

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromQuery(Name = "folderId")]
    private Guid? FolderId { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var fileResponse = await GetApiClient<FileClient>().GetByIdAsync(Id);
        Model = fileResponse?.Data;
    }
}
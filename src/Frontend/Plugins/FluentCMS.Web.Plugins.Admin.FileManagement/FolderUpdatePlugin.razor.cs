namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderUpdatePlugin
{
    public const string FORM_NAME = "FolderUpdateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private FolderUpdateRequest Model { get; set; }

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private List<AssetDetailResponse> Folders { get; set; }

    protected override async Task OnInitializedAsync()
    {
        var assetsResponse = await GetApiClient<FolderClient>().GetAllAsync(null);
        Folders = assetsResponse?.Data.Where(x => x.Id != Id && x.Type == AssetType.Folder).ToList() ?? [];

        if (Model is null)
        {
            var folderResponse = await GetApiClient<FolderClient>().GetByIdAsync(Id);
            var folder = folderResponse.Data;
            Model = Mapper.Map<FolderUpdateRequest>(folder);
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<FolderClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
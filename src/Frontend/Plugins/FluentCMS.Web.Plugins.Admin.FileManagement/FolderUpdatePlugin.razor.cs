namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderUpdatePlugin
{
    public const string FORM_NAME = "FolderUpdateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private FolderUpdateRequest Model { get; set; }

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private string Test { get; set; }

    private FolderDetailResponse RootFolder { get; set; }

    private List<FolderDetailResponse> Folders { get; set; } = [];

    private void AddFolders(ICollection<FolderDetailResponse> folders, string prefix)
    {
        foreach (var folder in folders)
        {
            if (folder.Id == Id)
                continue;

            folder.Name = $"{prefix} / {folder.Name}";
            Folders.Add(folder);

            if (folder.Folders != null && folder.Folders.Any())
                AddFolders(folder.Folders, $"{folder.Name}");
        }
    }

    protected override async Task OnInitializedAsync()
    {

        var rootFolderResponse = await GetApiClient<FolderClient>().GetAllAsync();
        RootFolder = rootFolderResponse?.Data;

        Folders = [RootFolder];
        AddFolders(RootFolder.Folders, RootFolder.Name);


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
namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileUpdatePlugin
{
    public const string FORM_NAME = "FileUpdateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private FileUpdateRequest Model { get; set; }

    private string Extension { get; set; }

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
            var fileResponse = await GetApiClient<FileClient>().GetByIdAsync(Id);
            var file = fileResponse.Data;
            Model = Mapper.Map<FileUpdateRequest>(file);

            Extension = System.IO.Path.GetExtension(Model.Name);
            Model.Name = Model.Name.Replace(Extension, "");
        }
    }

    private async Task OnSubmit()
    {
        Model.Name = Model.Name + Extension;
        await GetApiClient<FileClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
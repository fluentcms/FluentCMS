namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileUpdateModal
{
    [Parameter]
    public EventCallback<FileUpdateRequest> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public FileUpdateRequest Model { get; set; }

    private string Test { get; set; }

    [Parameter]
    public FolderDetailResponse RootFolder { get; set; }

    private List<FolderSelectOption> FolderOptions { get; set; } = [];

    private void AddFolders(ICollection<FolderDetailResponse> folders, string prefix)
    {
        foreach (var folder in folders)
        {
            if (folder.Id == Model.Id)
                continue;

            FolderOptions.Add(new FolderSelectOption
            {
                Id = folder.Id,
                Name = $"{prefix} / {folder.Name}"
            });

            if (folder.Folders != null && folder.Folders.Any())
                AddFolders(folder.Folders, $"{folder.Name}");
        }
    }

    protected override async Task OnInitializedAsync()
    {
        FolderOptions = [
            new FolderSelectOption
            {
                Id = RootFolder.Id,
                Name = RootFolder.Name,
            }
        ];
        AddFolders(RootFolder.Folders, RootFolder.Name);
    }

    private async Task HandleSubmit()
    {
        OnSubmit.InvokeAsync(Model);
    }

    private async Task HandleCancel()
    {
        OnCancel.InvokeAsync();
    }
}

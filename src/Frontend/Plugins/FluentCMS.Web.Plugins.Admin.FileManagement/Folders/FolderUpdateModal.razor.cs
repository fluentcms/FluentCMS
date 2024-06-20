namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderUpdateModal
{
    [Parameter]
    public EventCallback<FolderUpdateRequest> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public FolderUpdateRequest Model { get; set; }

    private string Test { get; set; }

    [Parameter]
    public FolderDetailResponse RootFolder { get; set; }

    private List<FolderDetailResponse> Folders { get; set; } = [];

    private void AddFolders(ICollection<FolderDetailResponse> folders, string prefix)
    {
        foreach (var folder in folders)
        {
            if (folder.Id == Model.Id)
                continue;

            folder.Name = $"{prefix} / {folder.Name}";
            Folders.Add(folder);

            if (folder.Folders != null && folder.Folders.Any())
                AddFolders(folder.Folders, $"{folder.Name}");
        }
    }

    protected override async Task OnInitializedAsync()
    {
        Folders = [RootFolder];
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

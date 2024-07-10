namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderUpdateModal
{
    [Parameter]
    public EventCallback<FolderUpdateRequest> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public FolderUpdateRequest Model { get; set; } = new();

    [Parameter]
    public FolderDetailResponse? RootFolder { get; set; }

    private List<FolderDetailResponse> Folders { get; set; } = [];

    private async Task AddFolders(ICollection<FolderDetailResponse>? folders, string? prefix)
    {
        if (folders is null || string.IsNullOrEmpty(prefix))
            return;

        foreach (var folder in folders)
        {
            if (folder.Id == Model.Id)
                continue;

            folder.Name = $"{prefix} / {folder.Name}";
            Folders.Add(folder);

            if (folder.Folders != null && folder.Folders.Any())
                await AddFolders(folder.Folders, $"{folder.Name}");
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (RootFolder != null)
        {
            Folders = [RootFolder];
            await AddFolders(RootFolder.Folders, RootFolder.Name);
        }
    }

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync(Model);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}

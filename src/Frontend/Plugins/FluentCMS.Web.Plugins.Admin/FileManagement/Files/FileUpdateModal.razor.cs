namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileUpdateModal
{
    [Parameter]
    public EventCallback<FileUpdateRequest> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public FileUpdateRequest Model { get; set; } = default!;

    [Parameter, EditorRequired]
    public FolderDetailResponse RootFolder { get; set; } = default!;

    private List<FolderSelectOption> FolderOptions { get; set; } = [];

    private async Task AddFolders(ICollection<FolderDetailResponse>? folders, string? prefix)
    {
        if (folders is null || string.IsNullOrEmpty(prefix))
            return;

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
                await AddFolders(folder.Folders, $"{folder.Name}");
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (RootFolder != null)
        {
            FolderOptions = [
                new FolderSelectOption
                {
                    Id = RootFolder.Id,
                    Name = RootFolder.Name ?? string.Empty,
                }
            ];
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

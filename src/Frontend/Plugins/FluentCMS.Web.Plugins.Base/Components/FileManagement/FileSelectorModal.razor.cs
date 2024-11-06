namespace FluentCMS.Web.Plugins;

public partial class FileSelectorModal
{
    [Parameter]
    public EventCallback<string> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public Guid? FolderId { get; set; }

    [Parameter]
    public Guid? Model { get; set; } = default!;

    private List<FolderDetailResponse> ParentFolders { get; set; } = [];

    private FolderDetailResponse? RootFolder { get; set; }

    private async Task NavigateFolder(Guid id)
    {
        FolderId = id;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private string GetDownloadUrl(AssetDetail file)
    {
        return string.Join("/", ParentFolders.Select(x => x.Name)) + "/" + file.Name;
    }


    private async Task OnSelectFile(AssetDetail item)
    {
        if (item.IsFolder) return;

        Model = item.Id;
        await OnSubmit.InvokeAsync(GetDownloadUrl(item));
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}

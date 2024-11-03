namespace FluentCMS.Web.Plugins;

public partial class FileSelectorModal
{
    [Parameter]
    public EventCallback<Guid> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public Guid? FolderId { get; set; }
    
    [Parameter]
    public Guid? Model { get; set; } = default!;

    private async Task NavigateFolder(Guid id)
    {
        FolderId = id;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private async Task DownloadFile(Guid id)
    {
        // TODO: download file
        await Task.CompletedTask;
    }


    private async Task OnSelectFile(AssetDetail item)
    {
        if (item.IsFolder) return;

        Model = item.Id;
        await OnSubmit.InvokeAsync(item.Id);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}

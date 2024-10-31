namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderRenameModal
{
    [Parameter]
    public EventCallback<FolderRenameRequest> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public FolderRenameRequest Model { get; set; } = new();

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync(Model);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}

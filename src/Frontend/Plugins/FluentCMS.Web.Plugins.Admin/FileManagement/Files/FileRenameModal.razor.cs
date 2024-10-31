namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FileRenameModal
{
    [Parameter]
    public EventCallback<FileRenameRequest> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public FileRenameRequest Model { get; set; } = default!;

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync(Model);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}

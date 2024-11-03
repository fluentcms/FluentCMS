namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderSelectorModal
{
    [Parameter]
    public EventCallback<Guid> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public Guid? DisabledFolder { get; set; }
    
    [Parameter]
    public Guid? Model { get; set; } = default!;

    private async Task OnChooseFolder()
    {
        if (Model is null) return;
        await OnSubmit.InvokeAsync(Model.Value);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}

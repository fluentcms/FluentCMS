namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderCreateModal
{
    [Parameter]
    public EventCallback<FolderCreateRequest> OnSubmit { get; set; }

    [Parameter]
    public Guid? FolderId { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    public FolderCreateRequest Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
    }

    private async Task HandleSubmit()
    {
        Model.FolderId = FolderId ?? Guid.Empty;
        await OnSubmit.InvokeAsync(Model);
        Model = new();
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
        Model = new();
    }
}

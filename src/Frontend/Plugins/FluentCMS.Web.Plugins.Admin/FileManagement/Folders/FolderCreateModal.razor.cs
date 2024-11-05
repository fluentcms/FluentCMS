namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderCreateModal
{
    [Parameter]
    public EventCallback<FolderCreateRequest> OnSubmit { get; set; }

    [Parameter]
    public Guid? FolderId { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    public FolderCreateRequest Model { get; set; } = new();

    private List<string> Errors { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await Task.CompletedTask;
    }

    private async Task HandleSubmit()
    {
        try 
        {
            Model.ParentId = FolderId ?? default!;
            await OnSubmit.InvokeAsync(Model);
            Model = new();
        }
        catch (ApiClientException ex)
        {
            Errors = ex.ApiResult?.Errors?.Select(x => $"{x.Code ?? string.Empty}: {x.Description ?? string.Empty}").ToList() ?? [ex.Message];
            StateHasChanged();
        }
        catch(Exception ex)
        {
            Errors = [ex.Message];
            StateHasChanged();
        }
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
        Model = new();
    }
}

namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public partial class FolderRenameModal
{
    [Parameter]
    public EventCallback<FolderRenameRequest> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public FolderRenameRequest Model { get; set; } = new();

    private List<string> Errors { get; set; } = [];

    private async Task HandleSubmit()
    {
        try
        {
            await OnSubmit.InvokeAsync(Model);
        }
        catch (ApiClientException ex)
        {
            Errors = ex.ApiResult?.Errors?.Select(x => $"{x.Code ?? string.Empty}: {x.Description ?? string.Empty}").ToList() ?? [ex.Message];
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Errors = [ex.Message];
            StateHasChanged();
        }
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }
}

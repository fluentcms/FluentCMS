namespace FluentCMS.Web.Plugins;

public partial class PluginModalForm
{
    [Parameter]
    public string? Title { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public bool Open { get; set; } = false;

    [Parameter]
    public string? Name { get; set; } = default!; // Form Name

    [Parameter]
    public virtual object? Model { get; set; }

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private List<string> Errors { get; set; } = [];

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

    private async Task HandleSubmit()
    {
        try
        {
            await OnSubmit.InvokeAsync();
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
}

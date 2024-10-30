namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class FieldSettingForm
{
    [Parameter, EditorRequired]
    public object? Model { get; set; } = default!;

    [CascadingParameter]
    public ContentTypeField? ContentTypeField { get; set; }

    [CascadingParameter]
    protected FieldManagementState CurrentState { get; set; }

    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    private List<string> Errors { get; set; } = [];

    protected async Task OnFormSubmit()
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

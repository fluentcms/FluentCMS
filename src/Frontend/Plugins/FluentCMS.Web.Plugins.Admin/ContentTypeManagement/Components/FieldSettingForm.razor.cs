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

    protected async Task OnFormSubmit()
    {
        await OnSubmit.InvokeAsync();
    }
}

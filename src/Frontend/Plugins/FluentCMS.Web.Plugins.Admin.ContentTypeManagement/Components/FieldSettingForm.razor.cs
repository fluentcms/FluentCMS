namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class FieldSettingForm<TModel>
{
    [Parameter, EditorRequired]
    public TModel Model { get; set; } = default!;

    [CascadingParameter]
    public ContentTypeField? ContentTypeField { get; set; }

    [CascadingParameter]
    protected FieldManagementState CurrentState { get; set; }

    [CascadingParameter]
    protected FieldType? FieldType { get; set; }

    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ContentTypeField> OnSubmit { get; set; }

    protected async Task OnFormSubmit()
    {
        await OnSubmit.InvokeAsync(Model?.ToContentTypeField());
    }
}

namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public abstract class BaseFieldSettingViewComponent<T> : BaseComponent where T : IFieldModel
{
    [Parameter, EditorRequired]
    public T Model { get; set; } = default!;

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

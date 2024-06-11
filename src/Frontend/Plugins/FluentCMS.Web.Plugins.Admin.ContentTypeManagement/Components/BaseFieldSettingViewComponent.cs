namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public abstract class BaseFieldSettingViewComponent<T, TField> : ComponentBase where TField : IFieldModel<T>
{
    [Parameter]
    public TField Model { get; set; } = default!;

    [Parameter]
    public ContentTypeField? ContentTypeField { get; set; }

    [CascadingParameter]
    protected FieldManagementState CurrentState { get; set; }

    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ContentTypeField> OnSubmit { get; set; }

    protected async Task OnFormSubmit()
    {
        await OnSubmit.InvokeAsync(Model?.ToContentTypeField<T, TField>());
    }
    protected override void OnInitialized()
    {
        base.OnInitialized();
    }

}

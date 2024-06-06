namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class BooleanFieldSettings
{

    [Parameter, EditorRequired]
    public BooleanFieldModel Model { get; set; } = default!;

    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ContentTypeField> OnSubmit { get; set; }

    private async Task OnFormSubmit()
    {
        await OnSubmit.InvokeAsync(Model?.ToContentTypeField());
    }
}

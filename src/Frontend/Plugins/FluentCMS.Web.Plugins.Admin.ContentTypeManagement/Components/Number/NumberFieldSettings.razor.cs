namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class NumberFieldSettings
{
    [Parameter, EditorRequired]
    public NumberFieldModel Model { get; set; } = default!;

    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ContentTypeField> OnSubmit { get; set; }

    public async Task OnFormSubmit()
    {
        await OnSubmit.InvokeAsync(Model?.ToContentTypeField());
    }
}

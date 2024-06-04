namespace FluentCMS.Admin.ContentTypeManagement;

public partial class ContentTypeFieldUpdate
{
    public const string FORM_NAME = "ContentTypeFieldUpdateForm";

    [Parameter]
    public ContentTypeField? Model { get; set; }

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync();
    }
}

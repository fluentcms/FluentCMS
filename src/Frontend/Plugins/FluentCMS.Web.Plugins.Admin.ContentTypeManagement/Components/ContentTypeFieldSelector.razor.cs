namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldSelector
{
    [Parameter]
    public EventCallback<FieldType> OnSelect { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    private async Task OnClick(FieldType type)
    {
        await OnSelect.InvokeAsync(type);
    }
}

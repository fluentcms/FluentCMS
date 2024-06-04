namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldSelector
{
    [Parameter]
    public EventCallback<FieldType> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    private async Task OnClick(FieldType type)
    {
        await OnSubmit.InvokeAsync(type);
    }

    private async Task OnCancelClicked()
    {
        await OnCancel.InvokeAsync();
    }
}

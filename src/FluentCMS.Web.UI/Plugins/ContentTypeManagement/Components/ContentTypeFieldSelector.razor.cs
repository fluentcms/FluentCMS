namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;
public partial class ContentTypeFieldSelector
{
    [Parameter]
    public EventCallback<string> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private async Task OnClick(FieldTypes fieldType)
    {
        var type = string.Empty;
        switch (fieldType)
        {
            case FieldTypes.Text:
                type = "text";
                break;

            case FieldTypes.TextArea:
                type = "textarea";
                break;

            case FieldTypes.Number:
                type = "number";
                break;

            case FieldTypes.Boolean:
                type = "boolean";
                break;

            default:
                break;
        }
        await OnSubmit.InvokeAsync(type);
    }

    private enum FieldTypes
    {
        Text,
        TextArea,
        Number,
        Boolean,
    }
}

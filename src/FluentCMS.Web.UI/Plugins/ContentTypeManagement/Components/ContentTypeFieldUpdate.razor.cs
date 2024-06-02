namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeFieldUpdate
{
    public const string FORM_NAME = "ContentTypeFieldUpdateForm";

    [Parameter]
    public ContentTypeField? Model { get; set; }

    [CascadingParameter]
    public List<FieldType> FieldTypes { get; set; }
    
    [Parameter]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

}

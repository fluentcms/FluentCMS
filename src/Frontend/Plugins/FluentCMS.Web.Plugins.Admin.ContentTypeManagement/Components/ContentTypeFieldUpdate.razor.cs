namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldUpdate
{
    public const string FORM_NAME = "ContentTypeFieldUpdateForm";

    [Parameter]
    public ContentTypeField? Model { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];
}

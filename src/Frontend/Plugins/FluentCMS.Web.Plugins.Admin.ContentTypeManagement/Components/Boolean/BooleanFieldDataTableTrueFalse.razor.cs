namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class BooleanFieldDataTableTrueFalse
{
    [CascadingParameter]
    public ContentTypeField? ContentTypeField { get; set; }

    [Parameter]
    public bool? Value { get; set; }
}

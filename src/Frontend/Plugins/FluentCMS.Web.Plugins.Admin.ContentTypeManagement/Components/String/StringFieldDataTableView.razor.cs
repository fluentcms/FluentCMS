namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class StringFieldDataTableView
{
    [CascadingParameter]
    public ContentTypeField? ContentTypeField { get; set; }

    [Parameter]
    public string? Value { get; set; }
}

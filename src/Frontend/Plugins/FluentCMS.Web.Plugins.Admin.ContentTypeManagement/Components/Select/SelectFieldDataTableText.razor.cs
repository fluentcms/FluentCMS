namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SelectFieldDataTableText
{
    [Parameter]
    public string? Value { get; set; }

    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }
}

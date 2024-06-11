namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SelectFieldDataTableBadge
{
    [Parameter]
    public string? Value { get; set; }

    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }
}

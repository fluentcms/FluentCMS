namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiSelectFieldDataTableBadges
{
    [Parameter]
    public ICollection<string> Value { get; set; }

    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }
}

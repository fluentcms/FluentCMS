namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class NumberFieldDataTableView
{
    [CascadingParameter]
    public ContentTypeField? ContentTypeField { get; set; }

    [Parameter]
    public decimal? Value { get; set; }
}

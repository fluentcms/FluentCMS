namespace FluentCMS.Web.UI.Components;

public partial class PaginationInfo
{
    [Parameter]
    public long Current { get; set; }

    [Parameter]
    public long Size { get; set; } = 10;

    [Parameter]
    public long Total { get; set; }
}
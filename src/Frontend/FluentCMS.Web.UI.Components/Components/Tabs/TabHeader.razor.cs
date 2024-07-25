namespace FluentCMS.Web.UI.Components;

public partial class TabHeader
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}

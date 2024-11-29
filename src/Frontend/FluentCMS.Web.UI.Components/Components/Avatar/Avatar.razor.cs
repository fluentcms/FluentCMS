namespace FluentCMS.Web.UI.Components;

public partial class Avatar
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}

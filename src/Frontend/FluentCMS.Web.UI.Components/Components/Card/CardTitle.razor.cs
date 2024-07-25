namespace FluentCMS.Web.UI.Components;

public partial class CardTitle
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}

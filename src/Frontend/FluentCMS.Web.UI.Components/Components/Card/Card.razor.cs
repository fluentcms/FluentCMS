namespace FluentCMS.Web.UI.Components;

public partial class Card
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}

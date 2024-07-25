namespace FluentCMS.Web.UI.Components;

public partial class CardActions
{
	[Parameter]
	public RenderFragment ChildContent { get; set; } = default!;
}

namespace FluentCMS.Web.UI.Components;

public partial class TabContent
{
	[Parameter]
	public RenderFragment ChildContent { get; set; } = default!;
}

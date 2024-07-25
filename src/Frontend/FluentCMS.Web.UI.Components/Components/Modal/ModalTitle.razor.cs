namespace FluentCMS.Web.UI.Components;

public partial class ModalTitle
{
	[Parameter]
	public RenderFragment ChildContent { get; set; } = default!;
}

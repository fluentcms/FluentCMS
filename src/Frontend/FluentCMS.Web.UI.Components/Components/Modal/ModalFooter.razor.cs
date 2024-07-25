namespace FluentCMS.Web.UI.Components;

public partial class ModalFooter
{
	[Parameter]
	public RenderFragment ChildContent { get; set; } = default!;
}

namespace FluentCMS.Web.UI.Components;

public partial class ModalBody
{
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
}

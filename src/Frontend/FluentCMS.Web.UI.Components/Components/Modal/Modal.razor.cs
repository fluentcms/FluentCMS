namespace FluentCMS.Web.UI.Components;

public partial class Modal
{
    [Parameter]
    [CSSProperty]
    public ModalSize Size { get; set; } = ModalSize.Medium;

    [Parameter]
    public EventCallback OnClose { get; set; }


    [Parameter]
    [CSSProperty]
    public ModalPlacement Placement { get; set; } = ModalPlacement.Middle;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    public async Task Close()
    {
        Visible = false;
        await OnClose.InvokeAsync();
    }
}

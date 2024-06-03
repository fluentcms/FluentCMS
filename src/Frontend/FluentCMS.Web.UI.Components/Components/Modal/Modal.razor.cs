namespace FluentCMS.Web.UI.Components;

public partial class Modal
{
    [Parameter]
    [CSSProperty]
    public ModalSize Size { get; set; } = ModalSize.Medium;

    [Parameter]
    public EventCallback OnClose { get; set; }

    public async void Close()
    {
        Visible = false;
        await OnClose.InvokeAsync();
    }
}
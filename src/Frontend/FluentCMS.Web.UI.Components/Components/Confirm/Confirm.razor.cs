namespace FluentCMS.Web.UI.Components;

public partial class Confirm
{
    [Parameter]
    [CSSProperty]
    public ModalSize Size { get; set; } = ModalSize.Medium;

    [Parameter]
    public EventCallback OnConfirm { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [JSInvokable]
    public async void CancelClicked()
    {
        Visible = false;
        await OnCancel.InvokeAsync();
    }

    public async void ConfirmClicked()
    {
        Visible = false;
        await OnConfirm.InvokeAsync();
    }
}

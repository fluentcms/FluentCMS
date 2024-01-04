namespace FluentCMS.Web.UI.Components.Core.Confirm;

public class ConfirmService
{
    public ConfirmProvider? ConfirmProvider { get; set; } = null;
    public void Show(string message)
    {
        ConfirmProvider?.Show(message);
    }
}

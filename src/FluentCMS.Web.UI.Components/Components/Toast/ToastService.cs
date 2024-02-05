namespace FluentCMS.Web.UI.Components;

public class ToastService
{
    public ToastProvider ToastProvider { get; set; } = default!;

    public void Info(string Message)
    {
        ToastProvider.Info(Message);
    }
}

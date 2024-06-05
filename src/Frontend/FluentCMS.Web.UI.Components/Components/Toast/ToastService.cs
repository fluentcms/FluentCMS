namespace FluentCMS.Web.UI.Components;

public class ToastService
{
    public ToastProvider ToastProvider { get; set; } = default!;

    public void Show(string Message)
    {
        ToastProvider.Show(Message, null);
    }

    public void Show(string Message, ToastType Type)
    {
        ToastProvider.Show(Message, Type);
    }
}

namespace FluentCMS.Web.UI.Components;

public partial class ToastProvider
{
    [Inject]
    ToastService toastService { get; set; } = default!;

    List<ToastModel> toasts = new List<ToastModel>();

    public void Show(string message, ToastType? type)
    {
        var toast = new ToastModel()
            {
                Duration = 5000,
                Message = message,
                Type = type ?? ToastType.Default
            };

        toasts.Add(toast);

        InvokeAsync(StateHasChanged);

        Timer? timer = null;

        timer = new Timer((_) =>
        {
            toasts.Remove(toast);

            InvokeAsync(StateHasChanged);

            timer?.Dispose();
        }, null, toast.Duration, -1);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        toastService.ToastProvider = this;
    }
}
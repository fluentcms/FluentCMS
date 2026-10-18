namespace FluentCMS.Admin.Components.Ui;

public sealed record Toast(Guid Id, string Title, string? Description, string Variant);

// One per circuit: a page calls Show and the Toaster on the same circuit renders it until it is closed or its time is up.
public sealed class ToastService(TimeProvider clock)
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    private readonly List<Toast> _toasts = [];
    private readonly Lock _lock = new();

    public event Action? Changed;

    public IReadOnlyList<Toast> Toasts
    {
        get
        {
            lock (_lock)
            {
                return [.. _toasts];
            }
        }
    }

    public void Show(string title, string? description = null, string variant = "default")
    {
        var toast = new Toast(Guid.NewGuid(), title, description, variant);
        lock (_lock)
        {
            _toasts.Add(toast);
        }
        Changed?.Invoke();
        _ = DismissLaterAsync(toast.Id);
    }

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (_lock)
        {
            removed = _toasts.RemoveAll(toast => toast.Id == id) > 0;
        }
        if (removed)
        {
            Changed?.Invoke();
        }
    }

    private async Task DismissLaterAsync(Guid id)
    {
        await Task.Delay(Duration, clock);
        Dismiss(id);
    }
}

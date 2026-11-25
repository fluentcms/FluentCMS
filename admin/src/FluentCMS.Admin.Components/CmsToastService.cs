namespace FluentCMS.Admin.Components;

// The toasts CmsToasts draws. Each is read once through the shell's live regions, an error from the assertive one, and the
// toast itself is no live region, which a screen reader may not read when it comes with its content (spec 4.18). An error
// stays until it is dismissed (WCAG 2.2.1); the others go after the lifetime.
internal sealed class CmsToastService(IAnnouncer? announcer, TimeProvider time) : ICmsToastService
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(7);

    private readonly List<CmsToast> _shown = [];

    public event Action? Changed;

    public IReadOnlyList<CmsToast> Shown => _shown;

    public Task ShowAsync(string message, CmsToastIntent intent = CmsToastIntent.Info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var toast = new CmsToast(message, intent);
        _shown.Add(toast);
        Changed?.Invoke();
        announcer?.Announce(message, intent == CmsToastIntent.Error ? AnnouncementPoliteness.Assertive : AnnouncementPoliteness.Polite);
        if (intent != CmsToastIntent.Error)
        {
            _ = DismissLaterAsync(toast);
        }

        return Task.CompletedTask;
    }

    public void Dismiss(CmsToast toast)
    {
        if (_shown.Remove(toast))
        {
            Changed?.Invoke();
        }
    }

    private async Task DismissLaterAsync(CmsToast toast)
    {
        await Task.Delay(Lifetime, time);
        Dismiss(toast);
    }
}

// A reference type, so that two toasts of the same text are two.
internal sealed class CmsToast(string message, CmsToastIntent intent)
{
    public string Message { get; } = message;

    public CmsToastIntent Intent { get; } = intent;
}

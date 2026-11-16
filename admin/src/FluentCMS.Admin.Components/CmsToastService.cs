using Microsoft.FluentUI.AspNetCore.Components;

namespace FluentCMS.Admin.Components;

internal sealed class CmsToastService(INotificationService notifications) : ICmsToastService
{
    public async Task ShowAsync(string message, CmsToastIntent intent = CmsToastIntent.Info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        await notifications.ShowToastAsync(options =>
        {
            options.Title = message;
            options.Intent = intent switch
            {
                CmsToastIntent.Success => ToastIntent.Success,
                CmsToastIntent.Warning => ToastIntent.Warning,
                CmsToastIntent.Error => ToastIntent.Error,
                _ => ToastIntent.Info,
            };
            options.Politeness = intent == CmsToastIntent.Error ? ToastPoliteness.Assertive : ToastPoliteness.Polite;
        });
    }
}

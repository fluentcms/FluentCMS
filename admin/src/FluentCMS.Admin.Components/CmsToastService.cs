using System.Net;
using FluentCMS.Admin.Components.Resources;
using Microsoft.FluentUI.AspNetCore.Components;

namespace FluentCMS.Admin.Components;

internal sealed class CmsToastService(INotificationService notifications, ICmsTextSource texts) : ICmsToastService
{
    public async Task ShowAsync(string message, CmsToastIntent intent = CmsToastIntent.Info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        await notifications.ShowToastAsync(options =>
        {
            // The library renders the title as sanitized HTML, which throws on a stray '<'; the message is text.
            options.Title = WebUtility.HtmlEncode(message);
            options.AllowDismiss = true;
            options.DismissAction.Label = texts.Text(CmsStrings.Close);
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

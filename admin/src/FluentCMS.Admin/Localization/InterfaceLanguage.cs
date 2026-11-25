using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Localization;

// The interface culture of the saved preference (spec 4.17): the browser's copy follows it, and when the app started in
// another culture than the preference chooses, the app reloads in it, with the unsaved forms kept as drafts, as at the end of
// a session. A reload the start asks for is made once per culture in the tab, so that a copy the browser does not give back
// cannot reload the tab again and again.
internal interface IInterfaceLanguage
{
    // True when the app reloads.
    Task<bool> FollowAsync(string? preferred, bool atStart);
}

internal sealed class InterfaceLanguage(
    InterfaceCulture culture,
    InterfaceCultureOptions options,
    ICultureStorage copy,
    ISessionStorage tab,
    FormDrafts drafts,
    IAdminSession session,
    NavigationManager navigation,
    ILogger<InterfaceLanguage> logger) : IInterfaceLanguage
{
    private string? _copy = culture.Copy;

    public async Task<bool> FollowAsync(string? preferred, bool atStart)
    {
        if (!string.Equals(_copy, preferred, StringComparison.OrdinalIgnoreCase))
        {
            if (!await copy.WriteAsync(preferred))
            {
                return false;
            }

            _copy = preferred;
        }

        var chosen = InterfaceCulture.Choose(preferred, culture.Browser, options).Name;
        if (chosen == InterfaceCulture.Choose(culture.Copy, culture.Browser, options).Name)
        {
            return false;
        }

        if (atStart)
        {
            if (await tab.GetAsync(TabStorageKeys.CultureReload) == chosen)
            {
                ShellLog.CultureNotApplied(logger);
                return false;
            }

            await tab.SetAsync(TabStorageKeys.CultureReload, chosen);
        }

        if (session.User is { } user)
        {
            await drafts.SaveTrackedAsync(user.Subject);
        }

        navigation.NavigateTo(navigation.Uri, forceLoad: true, replace: true);
        return true;
    }
}

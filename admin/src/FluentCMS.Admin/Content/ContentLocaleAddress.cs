using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Content;

// The content locale of a routed content page (spec 4.17): the locale its address names is set in the shell before the page
// reads, and the address of a localized type always names one, so that Back and a reload keep it. A switch the address made
// is announced and moves focus to the heading, since the link that made it is now text.
internal sealed class ContentLocaleAddress(
    AdminContext context,
    NavigationManager navigation,
    IAnnouncer announcer,
    DocumentInterop document,
    IStringLocalizer<AdminResources> localizer)
{
    // The locale the page showed after its last follow; null before the first.
    private string? shown;

    // Whether the shell is in the locale of the address, so that the page may read.
    public bool Ready { get; private set; }

    // The locale the page shows: null for the site's default.
    public string? Locale { get; private set; }

    public async Task FollowAsync(string type, string? named)
    {
        await context.UseLocaleAsync(named);
        if (named is null && context.ContentLocales.Count > 1 && context.Schema?.Types.FirstOrDefault(item => item.Name == type) is { Localized: true })
        {
            navigation.NavigateTo(navigation.GetUriWithQueryParameter("locale", context.ContentLocale), replace: true);
        }

        (Locale, Ready) = (context.NamedLocale, true);
        var before = shown;
        shown = context.ContentLocale;
        if (before is not null && before != shown && shown is not null)
        {
            announcer.Announce(localizer["Content.Locale.Switched", shown]);
            await document.FocusHeadingAsync();
        }
    }
}

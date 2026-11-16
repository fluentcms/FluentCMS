using FluentCMS.Admin.Components;
using FluentCMS.Admin.Shell;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FluentCMS.Admin.Interop;

// The theme the user chose, kept in the browser for every tab and the next visit (spec 4.1). Storage the browser refuses, as
// in some private windows, reads as no choice and keeps nothing.
internal interface IThemeStorage
{
    ValueTask<CmsThemeMode> ReadAsync();

    ValueTask WriteAsync(CmsThemeMode mode);
}

// The allow-listed user of IJSRuntime for localStorage (spec 3.1), through the browser's own functions. It writes one key and
// only the names light and dark, so that nothing else of the admin, and no token (AQ3), can reach localStorage through it;
// following the system is the key's absence.
internal sealed class ThemeStorageInterop(IJSRuntime js, ILogger<ThemeStorageInterop> logger) : IThemeStorage
{
    public const string Key = "fluentcms-admin/theme";

    public async ValueTask<CmsThemeMode> ReadAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", Key) switch
            {
                "light" => CmsThemeMode.Light,
                "dark" => CmsThemeMode.Dark,
                _ => CmsThemeMode.System,
            };
        }
        catch (JSException exception)
        {
            ShellLog.ThemeNotKept(logger, exception);
            return CmsThemeMode.System;
        }
    }

    public async ValueTask WriteAsync(CmsThemeMode mode)
    {
        try
        {
            await (mode switch
            {
                CmsThemeMode.Light => js.InvokeVoidAsync("localStorage.setItem", Key, "light"),
                CmsThemeMode.Dark => js.InvokeVoidAsync("localStorage.setItem", Key, "dark"),
                _ => js.InvokeVoidAsync("localStorage.removeItem", Key),
            });
        }
        catch (JSException exception)
        {
            ShellLog.ThemeNotKept(logger, exception);
        }
    }
}

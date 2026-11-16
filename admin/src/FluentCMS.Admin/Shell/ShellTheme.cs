using FluentCMS.Admin.Components;

namespace FluentCMS.Admin.Shell;

// The theme the user chose for the one CmsThemeRoot of AdminApp, which re-renders when it changes. It is not kept across a
// reload yet: the user menu of the shell (spec 4.1) stores it.
internal sealed class ShellTheme
{
    public CmsThemeMode Mode { get; private set; }

    public event Action? Changed;

    public void Choose(CmsThemeMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        Changed?.Invoke();
    }
}

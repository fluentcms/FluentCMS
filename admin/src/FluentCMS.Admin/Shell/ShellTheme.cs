using FluentCMS.Admin.Components;
using FluentCMS.Admin.Interop;

namespace FluentCMS.Admin.Shell;

// The theme the user chose for the one CmsThemeRoot of AdminApp, which re-renders when it changes. It follows the system until
// the user chooses, and the choice is kept in the browser.
internal sealed class ShellTheme(IThemeStorage storage)
{
    private bool _chosen;

    public CmsThemeMode Mode { get; private set; }

    public event Action? Changed;

    // At start; a choice made before the stored one is read wins.
    public async Task RestoreAsync()
    {
        var stored = await storage.ReadAsync();
        if (!_chosen)
        {
            Apply(stored);
        }
    }

    public async Task ChooseAsync(CmsThemeMode mode)
    {
        _chosen = true;
        Apply(mode);
        await storage.WriteAsync(mode);
    }

    private void Apply(CmsThemeMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        Changed?.Invoke();
    }
}

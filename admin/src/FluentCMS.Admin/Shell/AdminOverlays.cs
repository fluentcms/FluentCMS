using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Shell;

// What IAdminOverlays shows, in the order shown, which AdminOverlayOutlet draws.
internal sealed class AdminOverlays : IAdminOverlays
{
    private readonly List<Overlay> _shown = [];

    public event Action? Changed;

    public IReadOnlyList<Overlay> Shown => _shown;

    public IDisposable Show(RenderFragment content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var overlay = new Overlay(this, content);
        _shown.Add(overlay);
        Changed?.Invoke();
        return overlay;
    }

    internal sealed class Overlay(AdminOverlays owner, RenderFragment content) : IDisposable
    {
        public RenderFragment Content => content;

        public void Dispose()
        {
            if (owner._shown.Remove(this))
            {
                owner.Changed?.Invoke();
            }
        }
    }
}

using Microsoft.AspNetCore.Components;

namespace FluentCMS.Admin.Shell;

// What IAdminOverlays shows, in the order shown, which AdminOverlayOutlet draws.
internal sealed class AdminOverlays : IAdminOverlays
{
    private readonly List<Overlay> _shown = [];

    private int _outlets;

    public event Action? Changed;

    public IReadOnlyList<Overlay> Shown => _shown;

    public IDisposable Show(RenderFragment content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (_outlets == 0)
        {
            // Content nobody draws would wait for an answer forever, as a picker's would.
            throw new InvalidOperationException("The shell draws no overlays: AdminOverlayOutlet is not rendered.");
        }

        var overlay = new Overlay(this, content);
        _shown.Add(overlay);
        Changed?.Invoke();
        return overlay;
    }

    public void Attach() => _outlets++;

    public void Detach() => _outlets--;

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

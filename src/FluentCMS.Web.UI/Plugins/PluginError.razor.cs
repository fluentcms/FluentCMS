using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace FluentCMS.Web.UI.Plugins;

public partial class PluginError
{
    [Parameter]
    public string? Message { get; set; }

    [Parameter]
    public ErrorBoundary? ErrorBoundaryRef { get; set; }

    [Inject]
    public NavigationManager? NavigationManager { get; set; }

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);
        if (firstRender && NavigationManager != null)
        {
            NavigationManager.LocationChanged += NavigationManagerOnLocationChanged;
        }
    }

    private void NavigationManagerOnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        // recover component to be able to navigate
        Retry();
    }

    private void Retry()
    {
        ErrorBoundaryRef?.Recover();
    }

    public void Dispose()
    {
        if (NavigationManager != null)
        {
            NavigationManager.LocationChanged -= NavigationManagerOnLocationChanged;
        }
    }
}

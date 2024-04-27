using Microsoft.AspNetCore.Components.Web;

namespace FluentCMS.Web.UI.Plugins;

public partial class PluginError
{
    [Parameter]
    public string? Message { get; set; }

    [Parameter]
    public ErrorBoundary? ErrorBoundaryRef { get; set; }

    private void Retry()
    {
        ErrorBoundaryRef?.Recover();
    }
}

using Microsoft.AspNetCore.Components.Web;

namespace FluentCMS.Web.UI.Components;

public partial class PluginContainer
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public ErrorBoundary ErrorBoundaryRef { get; set; }
}

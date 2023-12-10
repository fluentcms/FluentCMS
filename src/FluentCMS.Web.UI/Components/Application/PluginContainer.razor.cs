using FluentCMS.Api.Models;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Application;

public partial class PluginContainer
{
    [CascadingParameter(Name = "Plugin")]
    public PluginResponse Plugin { get; set; } = default!;

    [CascadingParameter]
    public AppState AppState { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private bool ShouldRenderEdit()
    {
        return (string.IsNullOrEmpty(AppState?.ViewMode) || AppState?.ViewMode == "View")
            && !string.IsNullOrEmpty(Plugin.Definition.EditType);
    }

    private string? EditUrl => $"{AppState?.Page?.Path}?pluginId={Plugin.Id}&viewMode=edit";
}

using FluentCMS.Web.ApiClients;
using Microsoft.AspNetCore.Components;
using System.Web;

namespace FluentCMS.Web.UI;

public partial class PluginContainer
{
    [Parameter]
    public PluginDetailResponse Plugin { get; set; } = default!;

    [Parameter]
    public string SectionName { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private PluginLoader PluginLoader { get; set; } = default!;

    [CascadingParameter]
    private ViewContext ViewContext { get; set; } = default!;

    private IDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

    protected override void OnInitialized()
    {
        Parameters = new Dictionary<string, object>
        {
            { "SectionName", SectionName },
            { "Plugin", Plugin }
        };
    }

    private Type? GetPluginType()
    {
        PluginDefinitionType? pluginDefType;

        if (ViewContext.Type == ViewType.Default)
            pluginDefType = Plugin.Definition.Types?.Where(p => p.IsDefault).FirstOrDefault();
        else
            pluginDefType = Plugin.Definition?.Types?.Where(p => p!.Name!.Equals(ViewContext.PluginViewName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

        if (pluginDefType is null)
            throw new InvalidOperationException("Plugin definition type not found!");

        var assemblyName = Plugin?.Definition?.Assembly;
        if (string.IsNullOrEmpty(assemblyName))
            throw new InvalidOperationException("Plugin assembly name not found!");

        var type = PluginLoader.GetType(assemblyName, pluginDefType!.Type!) ??
            throw new InvalidOperationException("Plugin type not found!");

        return type;

    }
}

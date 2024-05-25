using System.Web;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class Section
{
    [Parameter]
    // this will be set while dynamically rendering the template
    public string Name { get; set; } = default!;

    [Parameter]
    // this will be set while dynamically rendering the template
    public PageFullDetailResponse? Page { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private Type? GetType(PluginDetailResponse plugin)
    {
        var uri = new Uri(NavigationManager.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);

        var assembly = typeof(Section).Assembly;
        var pluginTypeName = query["typeName"];
        PluginDefinitionType? pluginDefType;

        if (string.IsNullOrEmpty(pluginTypeName))
            pluginDefType = plugin.Definition.Types?.Where(p => p.IsDefault).FirstOrDefault();
        else
            pluginDefType = plugin.Definition?.Types?.Where(p => p.Name.Equals(pluginTypeName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

        if (pluginDefType is null)
            throw new InvalidOperationException("Plugin definition type not found!");

        return assembly.DefinedTypes.FirstOrDefault(x => x.Name == pluginDefType.Type);
    }
}

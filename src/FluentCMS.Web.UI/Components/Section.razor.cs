using System.Web;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class Section
{
    [Parameter]
    public string Name { get; set; } = default!;

    [Parameter]
    public PageFullDetailResponse? Page { get; set; }

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    private Type? GetType(PluginDetailResponse plugin)
    {
        var uri = new Uri(NavigationManager.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);

        var assembly = typeof(Section).Assembly;
        var pluginTypeName = query["typeName"];
        PluginDefinitionType pluginDefType;

        if (string.IsNullOrEmpty(pluginTypeName))
            pluginDefType = plugin.Definition?.Types?.Where(p => p.IsDefault).FirstOrDefault();
        else
            pluginDefType = plugin.Definition?.Types?.Where(p => p.Name.Equals(pluginTypeName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

        return assembly.DefinedTypes.FirstOrDefault(x => x.Name == pluginDefType.Type);
    }
}

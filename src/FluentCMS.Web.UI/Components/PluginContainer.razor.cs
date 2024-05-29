using System.Web;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginContainer
{
    [Parameter]
    public PluginDetailResponse Plugin { get; set; } = default!;

    [Parameter]
    public string SectionName { get; set; } = default!;

    private IDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();


    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

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
        var uri = new Uri(NavigationManager.Uri);
        var query = HttpUtility.ParseQueryString(uri.Query);

        var assembly = typeof(Section).Assembly;
        var pluginTypeName = query["typeName"];
        PluginDefinitionType? pluginDefType;

        if (string.IsNullOrEmpty(pluginTypeName))
            pluginDefType = Plugin.Definition.Types?.Where(p => p.IsDefault).FirstOrDefault();
        else
            pluginDefType = Plugin.Definition?.Types?.Where(p => p.Name.Equals(pluginTypeName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

        if (pluginDefType is null)
            throw new InvalidOperationException("Plugin definition type not found!");

        return assembly.DefinedTypes.FirstOrDefault(x => x.Name == pluginDefType.Type);
    }
}

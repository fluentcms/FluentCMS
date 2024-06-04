using System.Web;

namespace FluentCMS.Web.UI;

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

        var pluginTypeName = query["typeName"];
        PluginDefinitionType? pluginDefType;

        if (string.IsNullOrEmpty(pluginTypeName))
            pluginDefType = Plugin.Definition.Types?.Where(p => p.IsDefault).FirstOrDefault();
        else
            pluginDefType = Plugin.Definition?.Types?.Where(p => p.Name.Equals(pluginTypeName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

        if (pluginDefType is null)
            throw new InvalidOperationException("Plugin definition type not found!");

        // find type with type name in all assemblies
        var typeName = pluginDefType.Type;
        var x = AppDomain.CurrentDomain.GetAssemblies().Where(y => y.GetName().FullName.Contains("FluentCMS")).ToList(); ;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().FullName.Contains("FluentCMS"))
            {
                var type = assembly.DefinedTypes.FirstOrDefault(x => x.Name == pluginDefType.Type);
                if (type is null)
                    continue;
                return type;
            }
        }
        return null;
        //var t = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.DefinedTypes).Where(x => x.Name.Equals(pluginDefType.Type, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
        //return t;
        //var x = assembly.DefinedTypes.FirstOrDefault(x => x.Name == pluginDefType.Type);
        //return x;
    }
}

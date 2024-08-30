namespace FluentCMS.Web.UI;

public partial class PluginContainer
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private PluginLoader PluginLoader { get; set; } = default!;

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    private IDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

    protected override void OnInitialized()
    {
        Console.WriteLine($"{GetPluginDefinitionType().Title} {Plugin}");
        Plugin.Title = GetPluginDefinitionType()?.Title ?? "Untitled";
        Parameters = new Dictionary<string, object>
        {
            { "Plugin", Plugin }
        };
    }

    private PluginDefinitionTypeViewState? GetPluginDefinitionType()
    {
        PluginDefinitionTypeViewState? pluginDefType;

        if (ViewState.Type == ViewStateType.Default || ViewState.Type == ViewStateType.PagePreview)
            pluginDefType = Plugin.Definition.Types?.Where(p => p.IsDefault).FirstOrDefault();
        else
            pluginDefType = ViewState.PluginDefinitionType;

        if (pluginDefType is null)
            throw new InvalidOperationException("Plugin definition type not found!");

        return pluginDefType;
    }

    private Type? GetPluginType()
    {
        var assemblyName = Plugin?.Definition?.Assembly;
        if (string.IsNullOrEmpty(assemblyName))
            throw new InvalidOperationException("Plugin assembly name not found!");

        var type = PluginLoader.GetType(assemblyName, GetPluginDefinitionType()!.Type!) ??
            throw new InvalidOperationException("Plugin type not found!");

        return type;
    }
}

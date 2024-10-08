namespace FluentCMS.Web.UI;

public partial class PluginContainer : IAsyncDisposable
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Inject]
    private PluginLoader PluginLoader { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    private IDictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
    private PluginSettingsModel Settings { get; set; } = new();

    private void Load()
    {
        Plugin.Settings.TryGetValue("Class", out var Class);
        Plugin.Settings.TryGetValue("Style", out var Style);

        Settings = new PluginSettingsModel {
            Class = Class ?? "",
            Style = Style ?? "",
        };

        StateHasChanged();
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        Load();
    }

    protected override void OnInitialized()
    {
        Parameters = new Dictionary<string, object>
        {
            { "Plugin", Plugin }
        };
        ViewState.OnStateChanged += OnStateChanged;
        Load();
    }

    public async ValueTask DisposeAsync()
    {
        ViewState.OnStateChanged -= OnStateChanged;
        await Task.CompletedTask;
    }

    private Type? GetPluginType()
    {
        PluginDefinitionTypeViewState? pluginDefType;

        if (ViewState.Type == ViewStateType.Default || ViewState.Type == ViewStateType.PagePreview)
            pluginDefType = Plugin.Definition.Types?.Where(p => p.IsDefault).FirstOrDefault();
        else
            pluginDefType = Plugin.Definition?.Types?.Where(p => p!.Name!.Equals(ViewState.PluginViewName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

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

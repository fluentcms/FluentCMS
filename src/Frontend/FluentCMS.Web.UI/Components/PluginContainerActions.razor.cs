namespace FluentCMS.Web.UI;

public partial class PluginContainerActions
{
    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    [Parameter]
    public EventCallback OnUpdate { get; set; } = default!;

    [Inject]
    private PluginLoader PluginLoader { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClients { get; set; } = default!;

    #region Plugin Edit
    private bool EditModalOpen { get; set; } = false;

    private Type? GetPluginType(string typeName)
    {
        PluginDefinitionTypeViewState? pluginDefType;

        pluginDefType = Plugin.Definition?.Types?.Where(p => p!.Name!.Equals(typeName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();

        if (pluginDefType is null)
            throw new InvalidOperationException("Plugin definition type not found!");

        var assemblyName = Plugin?.Definition?.Assembly;
        if (string.IsNullOrEmpty(assemblyName))
            throw new InvalidOperationException("Plugin assembly name not found!");

        var type = PluginLoader.GetType(assemblyName, pluginDefType!.Type!) ??
            throw new InvalidOperationException("Plugin type not found!");

        return type;
    }

    private async Task OpenEditModal()
    {
        EditModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task OnEditCancel()
    {
        EditModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task OnEditSubmit()
    {
        EditModalOpen = false;
        await OnUpdate.InvokeAsync();
    }

    private Dictionary<string, object> GetEditParameters()
    {
        Dictionary<string, object> result = new() 
        {
            { "Open", EditModalOpen },
            { "Plugin", Plugin },
            { "OnSubmit", EventCallback.Factory.Create(this, OnEditSubmit) },
            { "OnCancel", EventCallback.Factory.Create(this, OnEditCancel) },
        };

        return result;
    }

    #endregion

    #region Plugin Settings
    private bool SettingsModalOpen { get; set; } = false;
    private PluginSettingsModel SettingsModel { get; set; } = new();

    private async Task OpenSettingsModal()
    {
        Plugin.Settings.TryGetValue("Class", out var Class);
        Plugin.Settings.TryGetValue("Style", out var Style);

        SettingsModel = new()
        {
            Class = Class ?? "",
            Style = Style ?? "",
        };

        SettingsModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task OnSettingsClose()
    {
        SettingsModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task OnSettingsSubmit()
    {
        var request = new SettingsUpdateRequest
        {
            Id = Plugin.Id,
            Settings = new Dictionary<string, string>
            {
                {"Class", SettingsModel.Class},
                {"Style", SettingsModel.Style},
            }
        };

        await ApiClients.Settings.UpdateAsync(request);

        SettingsModalOpen = false;
        await OnUpdate.InvokeAsync();
    }

    #endregion

    #region Delete Plugin

    private bool DeleteConfirmModalOpen { get; set; } = false;

    private async Task OpenDeleteConfirm()
    {
        DeleteConfirmModalOpen = true;
        await Task.CompletedTask;
    }

    private async Task OnConfirmClose()
    {
        DeleteConfirmModalOpen = false;
        await Task.CompletedTask;
    }

    private async Task OnDeleteConfirm()
    {
        DeleteConfirmModalOpen = false;
        await ApiClients.Plugin.DeleteAsync(Plugin.Id);
        ViewState.Plugins.RemoveAll(x => x.Id == Plugin.Id);
        ViewState.StateChanged();
    }

    #endregion
}
